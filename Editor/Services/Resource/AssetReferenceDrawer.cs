using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// <see cref="AssetReference"/> 的检视器绘制：对象字段选中即写回 GUID，包名行绘制在其后（按 <see cref="ResourcePackageBridge"/> 的包名清单，有清单走下拉、无清单退回文本、后端不消费则不绘制，留空使用默认资源包），底部为路径与打包归属提示。
    /// </summary>
    /// <remarks>经反射闭合 <see cref="AssetInfoHelper"/> 的泛型绘制助手（编辑器一次查找后按资源类型缓存）。</remarks>
    [CustomPropertyDrawer(typeof(AssetReference), true)]
    public class AssetReferenceDrawer : PropertyDrawer
    {
        private const string GUID_PROPERTY = nameof(AssetReference.m_GUID);
        private const string PACKAGE_PROPERTY = nameof(AssetReference.m_PackageName);
        private const float LINE_SPACING = 2f;

        private static readonly GUIContent s_PackageContent =
            new GUIContent("Package", "资源包名称，留空使用默认资源包");

        /// <summary>包名下拉的首项（空串）：随时可回到"默认资源包"语义。</summary>
        private static readonly GUIContent s_DefaultPackageContent =
            new GUIContent("(Default Package)", "留空使用默认资源包");

        private static readonly Dictionary<Type, MethodInfo> s_HeightMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> s_FieldMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> s_HintMethods = new Dictionary<Type, MethodInfo>();

        #region 引擎方法 [UNITY METHODS]

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            Type assetType = ResolveAssetType(fieldInfo?.FieldType);
            if (assetType == null)
            {
                return EditorGUIUtility.singleLineHeight * 2;
            }

            // 对象字段一行 + 包名行（后端不消费包名时不绘制）（提示行由助手按内容追加）
            object[] args = { property.FindPropertyRelative(GUID_PROPERTY).stringValue, ResourcePackageBridge.GetPackageNames() != null ? 2 : 1 };
            return (float)GetHelperMethod(s_HeightMethods, nameof(AssetInfoHelper.GetGuidAssetInfoHeight), assetType)
                .Invoke(null, args);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Type assetType = ResolveAssetType(fieldInfo?.FieldType);
            if (assetType == null)
            {
                EditorGUI.HelpBox(position, "AssetReference requires a type argument: AssetReference<TObject>.",
                    MessageType.Warning);
                return;
            }

            // 对象字段行：选中即写回 GUID
            object[] fieldArgs = { position, property, GUID_PROPERTY };
            object target = GetHelperMethod(s_FieldMethods, nameof(AssetInfoHelper.DrawGuidAssetField), assetType)
                .Invoke(null, fieldArgs);
            position = (Rect)fieldArgs[0];

            // 包名行：绘制在对象字段之后（Addressables 这类单包后端不绘制）；有包名清单走下拉，无清单退回自由文本
            string[] packages = ResourcePackageBridge.GetPackageNames();
            if (packages != null)
            {
                Rect packageRect = new Rect(position) { height = EditorGUIUtility.singleLineHeight };
                SerializedProperty packageProperty = property.FindPropertyRelative(PACKAGE_PROPERTY);
                if (packages.Length > 0)
                {
                    DrawPackagePopup(packageRect, packageProperty, packages);
                }
                else
                {
                    packageProperty.stringValue = EditorGUI.TextField(packageRect, s_PackageContent, packageProperty.stringValue);
                }
                position.y += EditorGUIUtility.singleLineHeight + LINE_SPACING;
            }

            // 附加信息行：路径与打包归属提示
            object[] hintArgs = { position, target, property.FindPropertyRelative(GUID_PROPERTY).stringValue };
            GetHelperMethod(s_HintMethods, nameof(AssetInfoHelper.DrawGuidAssetHints), assetType).Invoke(null, hintArgs);
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]
        
        /// <summary>
        /// 包名下拉：首项恒为默认资源包（空串），其后为后端包名清单；当前值不在清单中（包裹被改名 / 删除）时插到默认项之后，同 <see cref="CollectorPackageDropdownAttributeDrawer"/> 的置顶约定。
        /// </summary>
        private static void DrawPackagePopup(Rect rect, SerializedProperty packageProperty, string[] packages)
        {
            var options = new List<string>(packages.Length + 1) { string.Empty };
            options.AddRange(packages);

            string current = packageProperty.stringValue;
            int index = current.Length == 0 ? 0 : options.IndexOf(current);
            if (index < 0)
            {
                options.Insert(1, current);
                index = 1;
            }

            var displayed = new GUIContent[options.Count];
            displayed[0] = s_DefaultPackageContent;
            for (int i = 1; i < options.Count; i++)
            {
                displayed[i] = new GUIContent(options[i]);
            }

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUI.Popup(rect, s_PackageContent, index, displayed);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(packageProperty.serializedObject.targetObject, "Change Package Name");
                packageProperty.stringValue = options[selected];
                packageProperty.serializedObject.ApplyModifiedProperties();
            }
        }

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

        private static MethodInfo GetHelperMethod(Dictionary<Type, MethodInfo> cache, string name, Type assetType)
        {
            if (cache.TryGetValue(assetType, out MethodInfo cached))
            {
                return cached;
            }

            MethodInfo open = typeof(AssetInfoHelper).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            MethodInfo closed = open?.MakeGenericMethod(assetType);
            cache[assetType] = closed;
            return closed;
        }
    }
}
