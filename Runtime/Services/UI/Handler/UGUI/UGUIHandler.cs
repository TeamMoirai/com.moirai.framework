using System;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 处理器（后端）：承载 UI 根的取用与常驻、面板装载与窗口实例创建。
    /// </summary>
    [ProviderDisplay(title: "uGUI 轨", description: "默认：Canvas 根 + UGUI 面板，UITK 壳也挂在这枚 UI 根下")]
    [Serializable]
    internal sealed class UGUIHandler : UIServiceHandler
    {
        // 核心字段
        [NonSerialized] private Transform _instanceRoot = null; // UI根节点变换组件
        [NonSerialized] private Camera _uiCamera = null; // UI专用摄像机
        [NonSerialized] private ErrorLogger _errorLogger; // 错误日志记录器
        [NonSerialized] private bool _rootAwaitingBind; // UI 根还没绑定或绑定失败，等每帧续等
        [NonSerialized] private bool _rootProblemLogged; // 当前这一轮等待已报过问题（缺绑定 / 缺 Canvas），避免每帧刷屏

        /// <summary>UI根节点。</summary>
        public override Transform UIRoot => _instanceRoot;

        /// <summary>UI专用摄像机。</summary>
        public override Camera UICamera => _uiCamera;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        /// <remarks>
        /// 共享栈与全局压制位的归零不在这里：那一份存储两支共用，归零收在门面的 <see cref="UIService.OnInit"/> 与 <see cref="UIService.OnShutdown"/> 那两处， <br />
        /// 本轨这一枚只复位自己持有的两个根位标志。 <br />
        /// 此阶段（BeforeSceneLoad）场景尚未加载，根绑定与错误日志判据延迟到首个 Update tick 取用。
        /// </remarks>
        protected override void OnInit()
        {
            _rootAwaitingBind = false;
            _rootProblemLogged = false;
            MainThreadDispatcher.Post(TryBindRoot);
        }

        /// <summary>
        /// 取用场景登记的 UI 根（<see cref="UIRootBinding.TryGetInstance()"/>）。
        /// </summary>
        /// <remarks>
        /// 尚未绑定、或已绑定但其下还没有 Canvas 时挂起等待，由 <see cref="Tick"/> 续等。 <br />
        /// 问题只在进入等待时报一次，续等期间静默重试。
        /// </remarks>
        internal void TryBindRoot()
        {
            var binding = UIRootBinding.TryGetInstance();
            if (binding != null && InitializeRoot(binding.gameObject))
            {
                _rootAwaitingBind = false;
                _rootProblemLogged = false;
                return;
            }

            _rootAwaitingBind = true;
            if (_rootProblemLogged)
            {
                return;
            }

            _rootProblemLogged = true;
            if (binding == null)
            {
                LogUtility.Error("UI 根尚未绑定：请在充当 UI 根的场景物体上挂 UIRootBinding。");
            }
            else
            {
                LogUtility.Fatal("Can't find any Canvas under UIRoot! Please add a Canvas first.");
            }
        }

        /// <summary>
        /// 绑定到具体 UI 根：取子层级 Canvas、置顶常驻、按调试器策略挂错误日志。
        /// </summary>
        /// <param name="uiRoot">已登记的 UI 根物体。</param>
        /// <returns>绑定成功为真；其下尚无 Canvas 时为假（调用方保持续等，Canvas 补上后可再试）。</returns>
        private bool InitializeRoot(GameObject uiRoot)
        {
            var canvas = uiRoot.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                return false;
            }

            _instanceRoot = canvas.transform;
            _uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            // EditMode 下 DontDestroyOnLoad 抛 InvalidOperationException——仅播放态常驻跨场景
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot.parent != null ? _instanceRoot.parent : _instanceRoot);
            }

            _instanceRoot.gameObject.layer = LayerMask.NameToLayer("UI");

            if (ShouldEnableErrorLog())
            {
                _errorLogger = new ErrorLogger();
            }

            return true;
        }

        /// <summary>
        /// 错误日志记录器的启用判据（跟随调试器窗口策略）。
        /// </summary>
        /// <returns>启用时为真，此时才构造并挂上 <see cref="ErrorLogger"/>。</returns>
        internal static bool ShouldEnableErrorLog()
            => ShouldEnableErrorLog(DebuggerService.ActiveWindowType, Debug.isDebugBuild, Application.isEditor);

        /// <summary>
        /// 错误日志记录器的启用判据（纯函数：入参已取出环境位，便于在不依赖 UI 后端与场景的前提下锁住判据方向）。
        /// </summary>
        /// <param name="activeWindowType">调试器窗口激活策略。</param>
        /// <param name="isDebugBuild">是否为开发（debug）构建。</param>
        /// <param name="isEditor">是否运行在编辑器内。</param>
        /// <returns>启用时为真。</returns>
        internal static bool ShouldEnableErrorLog(DebuggerActiveWindowType activeWindowType, bool isDebugBuild, bool isEditor)
        {
            switch (activeWindowType)
            {
                case DebuggerActiveWindowType.AlwaysOpen:
                    return true;

                case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                    return isDebugBuild;

                case DebuggerActiveWindowType.OnlyOpenInEditor:
                    return isEditor;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 本轨只认 uGUI 轨的窗：一次关停里 UI Toolkit 那一轨的窗留在共享栈上，由它自己那一枚驱动者去收。
        /// </summary>
        /// <param name="window">栈上待判的那一只。</param>
        /// <returns>这一只以 <see cref="UGUIWindow"/> 为基类时为真。</returns>
        protected override bool IsWindowOnOwnTrack(UIWindow window) => window is UGUIWindow;

        /// <summary>把这一枚注册进 uGUI 那一轨的门面槽——归属由本类自述，门面入口不认识任何一枚具体实现。</summary>
        internal override void Internal_Register() => UIService.Internal_ClaimUGUITrack(this);

        /// <summary>
        /// 处理器关闭：清理错误日志系统、关掉本轨那半边的窗，再销毁 UI 根节点。
        /// </summary>
        /// <remarks>
        /// 只交自己那一轨的窗进共享栈的关闭流程（<see cref="UIServiceHandler.CloseOwnTrackWindows"/>），另一轨的窗留在栈上。 <br />
        /// UI Toolkit 那一轨的壳挂在本轨这枚 UI 根下：销毁根之前那些壳必须由门面先关停收掉， <br />
        /// 这一次序归门面的 <see cref="UIService.OnShutdown"/>（它先叫 UI Toolkit 那一枚、再叫这一枚）。
        /// </remarks>
        protected override void OnShutdown()
        {
            if (_errorLogger != null)
            {
                _errorLogger.Dispose();
                _errorLogger = null;
            }
            CloseOwnTrackWindows(true);
            if (_instanceRoot != null && _instanceRoot.parent != null)
            {
                // EditMode 下 Object.Destroy 只会报错不落账——测试夹具与编辑器工具走 DestroyImmediate
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_instanceRoot.parent.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_instanceRoot.parent.gameObject);
                }
            }

            base.OnShutdown();
            _instanceRoot = null;
            _uiCamera = null;
            _rootAwaitingBind = false;
            _rootProblemLogged = false;
        }

        /// <summary>
        /// 本轨的每帧职责：UI 根还没绑上时续等绑定——整条共享栈的结算不归这一轨驱动，门面每帧叫一次。
        /// </summary>
        /// <remarks>
        /// UI 根晚到（加加载入的场景、运行期实例化）时这一枚保持续等，绑上之后就静默：本轨没有别的帧职责。 <br />
        /// 根没绑好不影响另一轨的窗：栈上窗口的内部更新由门面交给共享持有者结算，与这一枚的根位无关。
        /// </remarks>
        /// <param name="elapseSeconds">逻辑经过的秒数（本轨不吃）。</param>
        /// <param name="realElapseSeconds">真实经过的秒数（本轨不吃）。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (_rootAwaitingBind)
            {
                TryBindRoot();
            }
        }

        #endregion

        #region 设置安全区域 [SET SAFE AREA]

        /// <summary>
        /// 设置屏幕安全区域（异形屏支持）。
        /// </summary>
        /// <param name="safeRect">安全区域。</param>
        public override void ApplyScreenSafeRect(Rect safeRect)
        {
            CanvasScaler scaler = UIRoot.GetComponentInParent<CanvasScaler>();
            if (scaler == null)
            {
                LogUtility.Error($"Not found {nameof(CanvasScaler)} !");
                return;
            }

            // Convert safe area rectangle from absolute pixels to UGUI coordinates
            float rateX = scaler.referenceResolution.x / Screen.width;
            float rateY = scaler.referenceResolution.y / Screen.height;
            float posX = (int)(safeRect.position.x * rateX);
            float posY = (int)(safeRect.position.y * rateY);
            float width = (int)(safeRect.size.x * rateX);
            float height = (int)(safeRect.size.y * rateY);

            float offsetMaxX = scaler.referenceResolution.x - width - posX;
            float offsetMaxY = scaler.referenceResolution.y - height - posY;

            // 注意：安全区坐标系的原点为左下角
            var rectTrans = UIRoot.transform as RectTransform;
            if (rectTrans != null)
            {
                rectTrans.offsetMin = new Vector2(posX, posY); //锚框状态下的屏幕左下角偏移向量
                rectTrans.offsetMax = new Vector2(-offsetMaxX, -offsetMaxY); //锚框状态下的屏幕右上角偏移向量
            }
        }

        #endregion

    }
}
