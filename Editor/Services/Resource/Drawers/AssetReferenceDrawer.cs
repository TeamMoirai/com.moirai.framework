using System;
using Moirai.Atropos.Editor;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// <see cref="AssetReference"/> 的 Odin 绘制器，单行绘制：对象字段选中即写回 GUID、
    /// 标签按字段名实宽自绘（tooltip 附解析路径）、控件框紧贴其后，
    /// 行尾「Package」包名下拉（后端不消费则不绘制，选项语义见 <see cref="DrawPackagePopup"/>）；
    /// GUID 悬空（资产已不存在）时行下报错。
    /// </summary>
    /// <remarks>
    /// 全管线经 Odin 接管（含 List 元素），字段无需再标 <c>[DrawWithUnity]</c>；
    /// 无 Unity 序列化后端（纯 Odin 宿主）或类型参数不可解析时回退默认绘制。
    /// </remarks>
    public class AssetReferenceDrawer : OdinValueDrawer<AssetReference>
    {
        private const string GUID_PROPERTY = nameof(AssetReference.m_GUID);
        private const string PACKAGE_PROPERTY = nameof(AssetReference.m_PackageName);

        private static readonly GUIContent s_PackageContent =
            new GUIContent("Package", "资源包名称，留空使用后端配置的默认资源包");

        #region 引擎方法 [UNITY METHODS]

        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (ValueEntry.SmartValue == null)
            {
                CallNextDrawer(label);
                return;
            }

            Type assetType = ResolveAssetType(ValueEntry.TypeOfValue);
            if (assetType == null)
            {
                CallNextDrawer(label);
                return;
            }

            SerializedProperty property = FindUnityProperty(Property);
            if (property == null)
            {
                CallNextDrawer(label);
                return;
            }

            SerializedProperty guidProp = property.FindPropertyRelative(GUID_PROPERTY);
            SerializedProperty packageProp = property.FindPropertyRelative(PACKAGE_PROPERTY);
            if (guidProp == null || packageProp == null)
            {
                CallNextDrawer(label);
                return;
            }

            string guid = guidProp.stringValue;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UObject current = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath(path, assetType);

            // 前缀标签式控件的标签列宽由宿主 labelWidth 决定（置 0 实测被当「默认 150」哨兵，无从收窄），
            // 窄检视面板内字段名与控件框之间会空出整段标签列、控件框被挤出可视区；
            // 故标签一律分离自绘、按字段名实宽自适应，控件框紧贴其后
            EditorGUILayout.BeginHorizontal();

            // 对象行：选中即写回 GUID
            GUIContent fieldLabel = BuildLabel(label, path);
            GUILayout.Label(fieldLabel, GUILayout.Width(EditorStyles.label.CalcSize(fieldLabel).x + 2f));

            Rect fieldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true));
            EditorGUI.BeginChangeCheck();
            UObject selected = EditorGUI.ObjectField(fieldRect, current, assetType, false);
            if (EditorGUI.EndChangeCheck())
            {
                guidProp.stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected));
                Apply(property);
            }

            if (ResourcePackageBridge.GetPackageNames() != null)
            {
                DrawPackagePopup(packageProp);
            }

            EditorGUILayout.EndHorizontal();

            // GUID 悬空：资产已不存在时行下报错
            if (current == null && !string.IsNullOrEmpty(guid))
            {
                EditorGUILayout.LabelField(
                    AssetInfoHelper.GetResultCodeInfo(AssetInfoHelper.AssetCheckResult.FailAssetNotExist),
                    CustomStyles.ErrorTextStyle);
            }
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 从字段类型解析 <c>AssetReference&lt;TObject&gt;</c> 的资源类型（兼容数组与泛型容器字段）。
        /// </summary>
        public static Type ResolveAssetType(Type fieldType)
        {
            if (fieldType == null)
            {
                return null;
            }

            // 兼容数组
            if (fieldType.IsArray)
            {
                fieldType = fieldType.GetElementType();
            }
            // 兼容 List<T>
            else if (fieldType.IsGenericType && fieldType.GetGenericArguments().Length > 0 &&
                     typeof(AssetReference).IsAssignableFrom(fieldType.GetGenericArguments()[0]))
            {
                fieldType = fieldType.GetGenericArguments()[0];
            }

            while (fieldType != null)
            {
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetReference<>))
                {
                    return fieldType.GetGenericArguments()[0];
                }

                fieldType = fieldType.BaseType;
            }

            return null;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 包名下拉：行与置顶规则取自 <see cref="ResourcePackagePopup"/>（带「留空」项，清单与 <see cref="ResourceServiceSettings"/> 的
        /// YooAsset 包名选择同源）；清单未就绪时置灰占位。
        /// </summary>
        private static void DrawPackagePopup(SerializedProperty packageProp)
        {
            GUIContent[] displayed = ResourcePackagePopup.Build(packageProp.stringValue, true, out int index, out bool manifestEmpty);

            using (new EditorGUI.DisabledScope(manifestEmpty))
            {
                // 标签与下拉分离绘制：前缀标签式弹窗的标签区宽由宿主（Odin）控制的 labelWidth 决定，窄行内会把按钮挤成零宽
                GUILayout.Label(s_PackageContent, GUILayout.Width(56f));

                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(index, displayed, GUILayout.MaxWidth(180));
                if (EditorGUI.EndChangeCheck() && !manifestEmpty && selected >= 0 && selected < displayed.Length)
                {
                    packageProp.stringValue = ResourcePackagePopup.Pick(displayed, true, selected);
                    packageProp.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(packageProp.serializedObject.targetObject);
                }
            }
        }

        /// <summary>
        /// 标签 tooltip 附资源解析路径；已有 tooltip 时保留。
        /// </summary>
        private static GUIContent BuildLabel(GUIContent label, string path)
        {
            if (label == null)
            {
                return GUIContent.none;
            }

            if (string.IsNullOrEmpty(path))
            {
                return label;
            }

            string tooltip = string.IsNullOrEmpty(label.tooltip) ? path : label.tooltip + "\n" + path;
            return new GUIContent(label.text, tooltip);
        }

        /// <summary>
        /// 取该属性的 Unity 序列化属性；托管引用路径带前导点时剥掉重试（与检视器回退约定一致）。
        /// </summary>
        private static SerializedProperty FindUnityProperty(Sirenix.OdinInspector.Editor.InspectorProperty property)
        {
            try
            {
                string path = property.UnityPropertyPath;
                if (!string.IsNullOrEmpty(path) && path.StartsWith("."))
                {
                    path = path.Substring(1);
                }

                var tree = property.Tree;
                return tree.UnitySerializedObject?.FindProperty(path)
                    ?? tree.GetUnityPropertyForPath(property.Path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 应用序列化改动并标脏目标资产。
        /// </summary>
        private static void Apply(SerializedProperty property)
        {
            property.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(property.serializedObject.targetObject);
        }

        #endregion

    }
}
