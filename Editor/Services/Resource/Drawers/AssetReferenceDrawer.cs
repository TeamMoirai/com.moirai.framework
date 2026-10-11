using System;
using System.Collections.Generic;
using Moirai.Atropos.Editor;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// <see cref="AssetReference"/> 的 Odin 绘制器，单行绘制：对象字段选中即写回 GUID（标签 tooltip 附解析路径）、
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

        /// <summary>「留空」状态项的标记文案（全角括号使其区别于真实包裹名）。</summary>
        private const string s_EmptyPackageText = "（留空 · 默认资源包）";

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

            EditorGUILayout.BeginHorizontal();

            // 对象行：选中即写回 GUID
            EditorGUI.BeginChangeCheck();
            UObject selected = EditorGUILayout.ObjectField(BuildLabel(label, path), current, assetType, false);
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
        /// 包名下拉：选项与 <see cref="ResourceServiceSettings"/> 的 YooAsset 包名选择同源
        /// （<see cref="CollectorPackageDropdownAttributeDrawer.CollectOptions"/>，当前值不在清单中时置顶），
        /// 顶部恒有「留空」状态项——留空即使用后端默认资源包；清单未就绪时置灰占位。
        /// </summary>
        private static void DrawPackagePopup(SerializedProperty packageProp)
        {
            string current = packageProp.stringValue;
            List<string> options = CollectorPackageDropdownAttributeDrawer.CollectOptions(current, out int index);
            bool manifestEmpty = options.Count == 0;

            string defaultName = ResourceServiceSettings.ResourceServiceHandler?.DefaultPackageName;
            var emptyContent = new GUIContent(s_EmptyPackageText,
                string.IsNullOrEmpty(defaultName)
                    ? "留空：运行时使用后端配置的默认资源包"
                    : $"留空：运行时使用后端默认资源包（{defaultName}）");

            if (!manifestEmpty)
            {
                options.Insert(0, string.Empty);
                index += 1; // 空串当前值经 CollectOptions 得 -1，+1 后恰落「留空」项
            }
            else
            {
                index = -1;
            }

            var displayed = manifestEmpty
                ? new[] { CollectorPackageDropdownAttributeDrawer.s_NoPackages }
                : new GUIContent[options.Count];
            if (!manifestEmpty)
            {
                displayed[0] = emptyContent;
                for (int i = 1; i < options.Count; i++)
                {
                    displayed[i] = new GUIContent(options[i]);
                }
            }

            using (new EditorGUI.DisabledScope(manifestEmpty))
            {
                // 标签与下拉分离绘制：前缀标签式弹窗的标签区宽由宿主（Odin）控制的 labelWidth 决定，窄行内会把按钮挤成零宽
                GUILayout.Label(s_PackageContent, GUILayout.Width(56f));

                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(index, displayed, GUILayout.MaxWidth(180));
                if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < options.Count)
                {
                    packageProp.stringValue = options[selected];
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
