// ReSharper disable InconsistentNaming
namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏时间外观（Facade）。
    /// <para>统一的静态时间访问入口，通过替换 <see cref="Handler"/> 即可在引擎时钟与虚拟时钟等时间源之间
    /// 零成本切换，调用方代码无需任何改动。Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成
    /// （线程安全懒加载，未显式设置时使用 <see cref="DefaultGameTimeHandler"/>）。</para>
    /// <para>虚拟时钟接缝：测试注入自定义 <see cref="GameTimeHandler"/>（覆写
    /// <see cref="GameTimeHandler.ScaledNow"/>/<see cref="GameTimeHandler.UnscaledNow"/>），
    /// Timer 等时间服务即获得与 Unity 主循环无关的确定性推进；生产代码使用缺省引擎时钟即可。</para>
    /// <para>帧内高频读取走每帧由 <see cref="StartFrame"/> 采样的静态字段（零虚调用开销）；
    /// 对精度敏感的服务（计时器等）经 <see cref="GameTimeHandler.ScaledNow"/>/<see cref="GameTimeHandler.UnscaledNow"/>
    /// 实时直读双精度时钟。</para>
    /// </summary>
    [HandlerHost(typeof(GameTimeHandler))]
    public static partial class GameTime
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 按缺省时间源创建默认处理器（引擎时钟）。
        /// </summary>
        /// <returns>默认游戏时间处理器实例。</returns>
        internal static GameTimeHandler CreateDefaultHandler()
        {
            return new DefaultGameTimeHandler();
        }

        // private static GameTimeHandler GetHandlerFromSettings() => GameAppSettings.GameTimeHandler;

        #endregion

        #region 公共 API [PUBLIC API]

        /// <summary>
        /// 此帧开始时的时间（只读）。
        /// </summary>
        public static float time;

        /// <summary>
        /// 从上一帧到当前帧的间隔（秒）（只读）。
        /// </summary>
        public static float deltaTime;

        /// <summary>
        /// timeScale从上一帧到当前帧的独立时间间隔（以秒为单位）（只读）。
        /// </summary>
        public static float unscaledDeltaTime;

        /// <summary>
        /// 执行物理和其他固定帧速率更新的时间间隔（以秒为单位）。
        /// <remarks>如MonoBehavior的MonoBehaviour.FixedUpdate。</remarks>
        /// </summary>
        public static float fixedDeltaTime;

        /// <summary>
        /// 自游戏开始以来的总帧数（只读）。
        /// </summary>
        public static float frameCount;

        /// <summary>
        /// timeScale此帧的独立时间（只读）。这是自游戏开始以来的时间（以秒为单位）。
        /// </summary>
        public static float unscaledTime;

        /// <summary>
        /// 采样一帧的时间。每帧由 <see cref="GameApp"/> 在 Tick 前调用，
        /// 从当前 <see cref="Handler"/> 拉取本帧时间快照填充上方静态字段。
        /// </summary>
        public static void StartFrame()
        {
            GameTimeHandler handler = Handler;
            time = handler.ScaledTime;
            deltaTime = handler.DeltaTime;
            unscaledDeltaTime = handler.UnscaledDeltaTime;
            fixedDeltaTime = handler.FixedDeltaTime;
            frameCount = handler.FrameCount;
            unscaledTime = handler.UnscaledTime;
        }

        #endregion
    }
}