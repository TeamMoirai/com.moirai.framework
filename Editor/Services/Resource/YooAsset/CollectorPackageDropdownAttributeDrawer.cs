using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using YooAsset.Editor;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// <see cref="YooAssetHandler.CollectorPackageDropdownAttribute"/> 的绘制器。
    /// 每次构建/绘制实时读取 YooAsset 收集器设置中的包裹名作为选项。
    /// 提供 IMGUI（OnGUI）与 UITK（CreatePropertyGUI）双路径，宿主自动二选一；
    /// Odin 上下文由 <see cref="CollectorPackageDropdownOdinDrawer"/> 接管（见其说明）。
    /// </summary>
    [CustomPropertyDrawer(typeof(YooAssetHandler.CollectorPackageDropdownAttribute), true)]
    internal sealed class CollectorPackageDropdownAttributeDrawer : PropertyDrawer
    {
        /// <summary>收集器配置缺失或未配置任何包裹时的占位项。</summary>
        internal static readonly GUIContent s_NoPackages = new GUIContent("(No collector packages configured)");

        #region IMGUI

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
            => DrawPopupIMGUI(position, property, label);

        /// <summary>
        /// IMGUI 行绘制：Unity OnGUI 与 <see cref="CollectorPackageDropdownOdinDrawer"/> 共用，
        /// 保证两种宿主下的选项、置顶与写回行为一致。
        /// </summary>
        internal static void DrawPopupIMGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label, "Use with string fields only.");
                return;
            }

            List<string> options = CollectOptions(property, out int index);

            var displayed = new GUIContent[options.Count == 0 ? 1 : options.Count];
            if (options.Count == 0)
            {
                displayed[0] = s_NoPackages;
            }
            else
            {
                for (int i = 0; i < options.Count; i++) displayed[i] = new GUIContent(options[i]);
            }

            using (new EditorGUI.DisabledScope(options.Count == 0))
            {
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(position, label, index, displayed);
                if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < options.Count)
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Change Package Name");
                    property.stringValue = options[selected];
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        #endregion

        #region UITK 支持 [UITK SUPPORT]

        /// <summary>
        /// UITK 入口：返回原生 <see cref="PopupField{T}"/>，
        /// 使标签与 UITK 窗口中的其他字段对齐（IMGUI 回退绘制会导致样式错位）。
        /// </summary>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                return base.CreatePropertyGUI(property);
            }

            string propPath = property.propertyPath;
            SerializedObject so = property.serializedObject;

            List<string> options = CollectOptions(property, out int index);
            if (options.Count == 0)
            {
                // 与 IMGUI 路径一致：无可用选项时显示置灰占位（PopupField 不接受空选项列表）
                var placeholder = new PopupField<string>(property.displayName,
                    new List<string> { s_NoPackages.text }, 0);
                placeholder.SetEnabled(false);
                return placeholder;
            }

            var popup = new PopupField<string>(property.displayName, options, index);
            popup.style.flexGrow = 1f;

            // 写入前重新 FindProperty，避免使用失效的 SerializedProperty（与 ProviderDropdown 同款保障）
            popup.RegisterValueChangedCallback(_ =>
            {
                so.Update();
                SerializedProperty fresh = so.FindProperty(propPath);
                if (fresh == null) return;

                Undo.RecordObject(so.targetObject, "Change Package Name");
                fresh.stringValue = popup.value;
                so.ApplyModifiedProperties();
            });

            return popup;
        }

        #endregion

        /// <summary>
        /// 实时读取收集器包裹名构建选项，并解析当前值的索引；
        /// 当前值已不在选项中（包裹被改名/删除）时临时置顶显示。IMGUI / UITK / Odin 共用。
        /// </summary>
        private static List<string> CollectOptions(SerializedProperty property, out int index)
            => CollectOptions(property.stringValue, out index);

        /// <summary>
        /// 同 <see cref="CollectOptions(SerializedProperty, out int)"/>，供无 Unity SerializedProperty 的
        /// Odin ValueEntry 回退路径使用。
        /// </summary>
        internal static List<string> CollectOptions(string current, out int index)
        {
            var options = new List<string>();
            if (BundleCollectorSettingData.HasSettingAsset())
            {
                foreach (BundleCollectorPackage package in BundleCollectorSettingData.Setting.Packages)
                {
                    if (!string.IsNullOrEmpty(package.PackageName)) options.Add(package.PackageName);
                }
            }

            index = options.IndexOf(current);
            if (index < 0 && !string.IsNullOrEmpty(current))
            {
                options.Insert(0, current);
                index = 0;
            }

            return options;
        }
    }

    /// <summary>
    /// Odin 原生 Drawer，为 <see cref="YooAssetHandler.CollectorPackageDropdownAttribute"/> 接管 Odin 绘制，
    /// 与 <see cref="ProviderDropdownOdinDrawer"/> 同一套宿主约定。<br/>
    /// 必须接管的原因：Odin 开启 UITK 集成（Preferences → Odin → General → Enable UIToolkit Support）时，
    /// 其 <c>UnityPropertyAttributeDrawer</c> 只要检测到 Unity 绘制器重写了 CreatePropertyGUI（按方法存在与否
    /// 静态判定，不看返回值）便会放弃 IMGUI OnGUI 路径，改走内嵌 UITK 元素——该内嵌在 FrameworkSettingsWindow
    /// 等自定义 IMGUI 宿主中会中断整帧布局，导致整个 Inspector 内容区静默空白。本 Drawer 使 Odin 上下文
    /// 永远走 IMGUI 行绘制，不再触达 Unity 绘制器的 UITK 路径。
    /// </summary>
    /// <remarks>
    /// 优先级与 <see cref="ProviderDropdownOdinDrawer"/> 一致（wrapper=10001），高于 Odin 默认
    /// managed reference drawer 和 DrawWithUnity(10000)。纯 Unity 宿主（OnGUI）与 UITK 宿主
    /// （CreatePropertyGUI）不受影响，仍由 <see cref="CollectorPackageDropdownAttributeDrawer"/> 双路径服务。
    /// </remarks>
    [DrawerPriority(0, 10001, 0)]
    internal sealed class CollectorPackageDropdownOdinDrawer
        : OdinAttributeDrawer<YooAssetHandler.CollectorPackageDropdownAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // 4.0.x 下 SerializeReference 子字段的 UnityPropertyPath 可能解析失败。
            // 此时不能 CallNextDrawer（会退化成普通字符串输入框，包裹下拉“失效”），
            // 改走 ValueEntry 驱动的 popup，与 ProviderDropdownOdinDrawer 同一套回退约定。
            SerializedProperty prop;
            try { prop = Property.Tree.GetUnityPropertyForPath(Property.UnityPropertyPath); }
            catch { prop = null; }

            GUIContent rowLabel = label ?? GUIContent.none;
            if (prop != null)
            {
                Rect rowRect = EditorGUILayout.GetControlRect(
                    true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                CollectorPackageDropdownAttributeDrawer.DrawPopupIMGUI(rowRect, prop, rowLabel);
                return;
            }

            if (!DrawValueEntryFallback(rowLabel))
                CallNextDrawer(label);
        }

        /// <summary>
        /// 无 SerializedProperty 时：用 Odin ValueEntry 读写包裹名，仍从收集器设置实时取选项。
        /// </summary>
        private bool DrawValueEntryFallback(GUIContent rowLabel)
        {
            var valueEntry = Property.ValueEntry;
            if (valueEntry == null || valueEntry.TypeOfValue != typeof(string))
                return false;

            List<string> options = CollectorPackageDropdownAttributeDrawer
                .CollectOptions(valueEntry.WeakSmartValue as string, out int index);

            var displayed = new GUIContent[options.Count == 0 ? 1 : options.Count];
            if (options.Count == 0)
                displayed[0] = CollectorPackageDropdownAttributeDrawer.s_NoPackages;
            else
                for (int i = 0; i < options.Count; i++) displayed[i] = new GUIContent(options[i]);

            Rect rowRect = EditorGUILayout.GetControlRect(
                true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

            using (new EditorGUI.DisabledScope(options.Count == 0))
            {
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(rowRect, rowLabel, index, displayed);
                if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < options.Count)
                {
                    valueEntry.WeakSmartValue = options[selected];
                    Property.Update(true);
                    GUI.changed = true;
                }
            }

            return true;
        }
    }
}
