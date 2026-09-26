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

        // 六项快照一律 { get; private set; }：本类的写入方只有 StartFrame，留成 public 可写字段
        // 等于允许任意代码改写全局 deltaTime，且编译期无从追查来源。
        // 成员名保持 Unity Time.* 的小写风格（而非 C# 大写字母约定）：GameTime 是可替换时间源的
        // 调用方外观，改名会连带打破「换 Handler 即换时间源、调用方零改动」这一契约。

        /// <summary>
        /// 此帧开始时的时间。
        /// </summary>
        public static float time { get; private set; }

        /// <summary>
        /// 从上一帧到当前帧的间隔（秒）。
        /// </summary>
        public static float deltaTime { get; private set; }

        /// <summary>
        /// timeScale 从上一帧到当前帧的独立时间间隔（以秒为单位）。
        /// </summary>
        public static float unscaledDeltaTime { get; private set; }

        /// <summary>
        /// 执行物理和其他固定帧速率更新的时间间隔（以秒为单位）。
        /// <para>如 MonoBehaviour.FixedUpdate 所使用的步长。</para>
        /// </summary>
        public static float fixedDeltaTime { get; private set; }

        /// <summary>
        /// 自游戏开始以来的总帧数。
        /// </summary>
        /// <remarks>
        /// 类型为 <see cref="int"/> 而非旧声明的 <c>float</c>：float 尾数只有 24 位，
        /// 超过 16,777,216 帧后无法精确表示计数（120fps 下约 39 小时连续运行）。
        /// </remarks>
        public static int frameCount { get; private set; }

        /// <summary>
        /// timeScale 此帧的独立时间（以秒为单位），即自游戏开始以来的非缩放时间。
        /// </summary>
        public static float unscaledTime { get; private set; }

        /// <summary>
        /// 采样一帧的时间。每帧由 <see cref="PlayerLoopDriver"/> 在各 Drive 阶段入口调用，
        /// 从当前 <see cref="Handler"/> 拉取本帧时间快照填充上方静态属性。
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