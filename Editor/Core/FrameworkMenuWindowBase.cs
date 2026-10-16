using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// Odin 菜单窗口共享骨架：信息头样式、域重载建树守护、防抖重建、异步装载接入（冷装载遮罩、完成裁决）与工具栏按钮助手。
    /// </summary>
    /// <remarks>
    /// 域重载回调里 EditorStyles 必未就绪（访问即抛 NRE），建树不得以它为前提——子类 OnEnable 调
    /// <see cref="EnsureMenuTreeBuilt"/> 无条件建树，样式缺席自动记待补重建，OnImGUI 就绪后补建。
    /// 接入异步装载的派生窗在冷装载（无可展示数据）时由基类绘制整窗遮罩（进度与进度条）并禁用输入，数据在屏的重扫不遮罩；完成回调经裁决单次重建树再置就绪，遮罩与树同拍切换不露空树帧。
    /// </remarks>
    public abstract class FrameworkMenuWindowBase : OdinMenuEditorWindow
    {

        #region 信息头样式 [HEADER STYLES]

        /// <summary>
        /// 信息头 GUIStyle 懒初始化：由基类 OnImGUI 每拍统一驱动，派生窗无需自调。域重载早期 EditorStyles 未就绪时跳过，等下一帧重试。
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

        // delayCall 防抖：同帧多次触发只重建一次
        private bool _rebuildQueued;

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

        /// <summary>按值在树中选中条目，走 Odin 侧边栏点击同一条选中路径。</summary>
        /// <remarks>
        /// <c>TrySelectMenuItemWithObject</c> 只登记请求，要等下一拍 Layout 遍才落地；点击后要当下切换就同步选中。
        /// 选中变更本身即请绘，故在 GUI 事件外调用也立即上屏；树中没有该值（值未上树）时交出 <c>false</c>。
        /// </remarks>
        protected bool SelectMenuItemWithValue(object value)
        {
            OdinMenuTree tree = MenuTree;
            if (tree == null || value == null) return false;

            foreach (OdinMenuItem item in tree.EnumerateTree())
            {
                if (!ReferenceEquals(item.Value, value)) continue;
                item.Select();
                return true;
            }

            return false;
        }

        /// <summary>重建前回调：缓存待恢复的选中或失效相关缓存；默认无操作。</summary>
        protected virtual void OnBeforeTreeRebuild(object previousSelection) { }

        /// <summary>重建后回调：按需恢复选中；默认无操作。</summary>
        protected virtual void OnAfterTreeRebuild(object previousSelection) { }

        #endregion

        #region 异步装载 [ASYNC DATA LOAD]

        private EditorLoadPipeline _dataLoad;
        private bool _dataLoadCompletedOnce;

        /// <summary>最近一次数据装载管线；未启动过为 null。</summary>
        protected EditorLoadPipeline DataLoad => _dataLoad;

        /// <summary>数据装载是否在途。</summary>
        protected bool IsDataLoading => _dataLoad != null && _dataLoad.IsRunning;

        /// <summary>最近一次装载是否失败（异常详情见编辑器 Console）。</summary>
        protected bool IsDataFailed => _dataLoad != null && _dataLoad.IsFailed;

        /// <summary>数据是否就绪（至少成功装载过一次；重扫在途时旧数据仍视作就绪）。派生窗可扩展（如热缓存接管）。</summary>
        protected virtual bool IsDataReady => _dataLoadCompletedOnce;

        /// <summary>
        /// 启动（或重启）异步数据装载：取消在途管线后由子类经 <see cref="BuildDataLoadSteps"/> 登记步骤。
        /// </summary>
        protected void StartDataLoad()
        {
            _dataLoad?.Cancel();

            var pipeline = new EditorLoadPipeline(OnDataLoadCompleted, OnDataLoadFailed,
                () => { if (this != null) Repaint(); }, () => this != null);
            _dataLoad = pipeline;
            BuildDataLoadSteps(pipeline);
            pipeline.Start();
            Repaint();
        }

        /// <summary>
        /// 子类登记装载步骤；装载中可在主线程步内继续登记后续步。默认无步骤（不接入异步装载的派生窗零成本）。
        /// </summary>
        protected virtual void BuildDataLoadSteps(EditorLoadPipeline pipeline) { }

        /// <summary>全部步骤完成（主线程）：经 <see cref="ShouldRebuildTreeAfterDataLoad"/> 裁决单次重建后置就绪——遮罩随就绪同拍揭下，不露空树帧。</summary>
        protected virtual void OnDataLoadCompleted()
        {
            if (ShouldRebuildTreeAfterDataLoad())
            {
                RebuildTreePreservingSelection();
            }

            _dataLoadCompletedOnce = true;
            Repaint();
        }

        /// <summary>完成时是否重建树；默认恒真。在屏数据与新装载结果一致的派生窗可跳过，免无谓抖动。</summary>
        protected virtual bool ShouldRebuildTreeAfterDataLoad() => true;

        /// <summary>装载失败（主线程）：默认无操作，派生窗经 <see cref="IsDataFailed"/> 自行呈现。</summary>
        protected virtual void OnDataLoadFailed(Exception exception) { }

        protected override void OnDisable()
        {
            _dataLoad?.Cancel();
            base.OnDisable();
        }

        #endregion

        #region 装载遮罩 [LOAD OVERLAY]

        private const float LOAD_OVERLAY_BAR_WIDTH = 240f;

        // 压暗色与提示样式/内容：经就绪探针懒建，文案与尺寸只随进度变化重算（绘制期零分配）
        private static readonly Color s_LoadOverlayDimColor = new Color(0.08f, 0.08f, 0.08f, 0.55f);
        private static GUIStyle s_LoadOverlayLabel;
        private static GUIContent s_LoadOverlayContent;
        private static Vector2 s_LoadOverlaySize;

        /// <summary>
        /// 装载态整窗遮罩：压暗后居中呈现装载进度与进度条；装载期输入由 <see cref="OnImGUI"/> 的禁用域拦截，失败态由派生窗自行呈现。
        /// </summary>
        protected virtual void DrawDataLoadOverlay()
        {
            // 文案与样式依赖 EditorStyles，未就绪（域重载早期）跳过本拍，装载仍在途下一拍补上
            if (!IsEditorStylesReady()) return;

            EditorLoadPipeline load = DataLoad;
            if (load == null) return;

            if (s_LoadOverlayLabel == null)
            {
                s_LoadOverlayLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 14,
                    normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
                };
                s_LoadOverlayContent = new GUIContent();
            }

            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), s_LoadOverlayDimColor);

            string text = load.TotalCount > 0
                ? $"正在加载中 {load.ProcessedCount}/{load.TotalCount}"
                : "正在加载中...";
            if (s_LoadOverlayContent.text != text)
            {
                s_LoadOverlayContent.text = text;
                s_LoadOverlaySize = s_LoadOverlayLabel.CalcSize(s_LoadOverlayContent);
            }

            var labelRect = new Rect(position.width * 0.5f - s_LoadOverlaySize.x * 0.5f,
                position.height * 0.5f - s_LoadOverlaySize.y - 24f, s_LoadOverlaySize.x, s_LoadOverlaySize.y);
            GUI.Label(labelRect, s_LoadOverlayContent, s_LoadOverlayLabel);

            var barRect = new Rect(position.width * 0.5f - LOAD_OVERLAY_BAR_WIDTH * 0.5f, labelRect.yMax + 12f,
                LOAD_OVERLAY_BAR_WIDTH, 4f);
            EditorGUI.ProgressBar(barRect, load.Progress, string.Empty);
        }

        #endregion

        #region 绘制与工具栏 [DRAWING & TOOLBAR]

        protected override void OnImGUI()
        {
            HeaderStyles.Init();

            // 样式就绪后补一次重建，取回建树时没套上的菜单样式
            if (_menuStylePending && IsEditorStylesReady())
            {
                _menuStylePending = false;
                QueueTreeRebuild();
            }

            // 冷装载 = 无可展示数据：遮罩 + 禁输入；数据在屏的重扫（刷新/热缓存重开）不遮罩不锁交互
            if (IsDataLoading && !IsDataReady)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    base.OnImGUI();
                }

                DrawDataLoadOverlay();
            }
            else
            {
                base.OnImGUI();
            }
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
