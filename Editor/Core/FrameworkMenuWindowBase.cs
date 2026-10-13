using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// Odin 菜单窗口共享骨架：信息头样式、域重载建树四件套、防抖重建与工具栏按钮助手。
    /// </summary>
    /// <remarks>
    /// 域重载回调里 EditorStyles 必未就绪（访问即抛 NRE），建树不得以它为前提——子类 OnEnable 调
    /// <see cref="EnsureMenuTreeBuilt"/> 无条件建树，样式缺席自动记待补重建，OnImGUI 就绪后补建。
    /// </remarks>
    public abstract class FrameworkMenuWindowBase : OdinMenuEditorWindow
    {

        #region 信息头样式 [HEADER STYLES]

        /// <summary>
        /// 信息头 GUIStyle 懒初始化。域重载早期 EditorStyles 未就绪时跳过，等下一帧重试。
        /// </summary>
        protected static class HeaderStyles
        {
            private static bool s_Initialized;

            public static GUIStyle HeaderBg;
            public static GUIStyle Title;
            public static GUIStyle Description;
            public static GUIStyle Meta;
            public static GUIStyle CountLabel;

            /// <summary>样式是否已就绪。</summary>
            public static bool IsReady => s_Initialized && Title != null;

            public static void Init()
            {
                if (IsReady) return;
                // 域重载早期 EditorStyles 访问可能抛 NRE，探针判定，未就绪等下一帧
                if (!IsEditorStylesReady()) return;

                s_Initialized = true;

                var headerBgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                headerBgTex.SetPixel(0, 0, new Color(0.158f, 0.158f, 0.158f));
                headerBgTex.Apply();

                HeaderBg = new GUIStyle
                {
                    normal = { background = headerBgTex },
                    padding = new RectOffset(14, 14, 8, 6)
                };

                Title = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 17,
                    normal = { textColor = new Color(0.95f, 0.95f, 0.95f) }
                };

                Description = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.60f, 0.60f, 0.60f) }
                };

                Meta = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
                };

                // 配合 ExpandHeight 铺满行高，垂直中心与按钮文字对齐
                CountLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        #endregion

        #region 域重载 [DOMAIN RELOAD]

        // 建树时 EditorStyles 未就绪：样式就绪后补一次重建取回加粗菜单样式
        private bool _menuStylePending;

        // delayCall 防抖：同帧多次触发只重建一次
        private bool _rebuildQueued;

        /// <summary>MenuTree 缺失时无条件建树；OnEnable 调用。</summary>
        protected void EnsureMenuTreeBuilt()
        {
            if (MenuTree == null)
                ForceMenuTreeRebuild();
        }

        /// <summary>EditorStyles 是否已就绪。域重载早期访问会抛 NRE（而非返回 null），须捕获判定。</summary>
        protected static bool IsEditorStylesReady()
        {
            try { return EditorStyles.label != null; }
            catch (NullReferenceException) { return false; }
        }

        #endregion

        #region 重建 [REBUILD]

        /// <summary>
        /// 防抖重建：经 delayCall 延迟到 GUI 事件外执行，避免 GUI 事件中途重建菜单树。
        /// </summary>
        protected void QueueTreeRebuild()
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                _rebuildQueued = false;
                if (this == null) return;
                RebuildTreePreservingSelection();
            };
        }

        /// <summary>重建菜单树：前后回调交子类缓存/恢复选中。</summary>
        protected void RebuildTreePreservingSelection()
        {
            object selection = MenuTree?.Selection.SelectedValue;
            OnBeforeTreeRebuild(selection);
            ForceMenuTreeRebuild();
            OnAfterTreeRebuild(selection);
        }

        /// <summary>重建前回调：缓存待恢复的选中或失效相关缓存；默认无操作。</summary>
        protected virtual void OnBeforeTreeRebuild(object previousSelection) { }

        /// <summary>重建后回调：按需恢复选中；默认无操作。</summary>
        protected virtual void OnAfterTreeRebuild(object previousSelection) { }

        #endregion

        #region 工具栏 [TOOLBAR]

        protected override void OnImGUI()
        {
            // 样式就绪后补一次重建，取回建树时没套上的菜单样式
            if (_menuStylePending && IsEditorStylesReady())
            {
                _menuStylePending = false;
                QueueTreeRebuild();
            }

            base.OnImGUI();
        }

        /// <summary>套用加粗菜单样式（含选中态）；EditorStyles 未就绪时记待补重建。BuildMenuTree 调用。</summary>
        protected void ApplyMenuStyle(OdinMenuTree tree)
        {
            if (!IsEditorStylesReady())
            {
                _menuStylePending = true;
                return;
            }

            _menuStylePending = false;

            var menuStyle = tree.DefaultMenuStyle.Clone();
            menuStyle.DefaultLabelStyle = new GUIStyle(menuStyle.DefaultLabelStyle) { fontStyle = FontStyle.Bold };
            menuStyle.SelectedLabelStyle = new GUIStyle(menuStyle.SelectedLabelStyle) { fontStyle = FontStyle.Bold };
            tree.DefaultMenuStyle = menuStyle;
        }

        /// <summary>工具栏按钮：统一样式与固定宽度。</summary>
        protected static bool ToolbarButton(string label, float width)
        {
            return GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(width));
        }

        #endregion

    }
}
