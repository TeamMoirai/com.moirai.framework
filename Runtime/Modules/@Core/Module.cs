namespace Moirai.Atropos
{
    /// <summary>
    /// 模块需要框架轮询。
    /// </summary>
    public interface IUpdateModule
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        void Update(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 模块需要框架轮询。
    /// </summary>
    public interface IFixedUpdateModule
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        void FixedUpdate(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 模块需要框架轮询。
    /// </summary>
    public interface ILateUpdateModule
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        void LateUpdate(float elapseSeconds, float realElapseSeconds);
    }

    /// <summary>
    /// 模块需要绘制 Gizmos。
    /// </summary>
    public interface IGizmoModule
    {
        /// <summary>
        /// 绘制 Gizmos。
        /// </summary>
        void OnDrawGizmos();
    }

    /// <summary>
    /// 模块生命周期作用域。
    /// </summary>
    public enum ModuleScope : byte
    {
        /// <summary>应用级，生命周期最长，适合资源、音频、UI、计时器等全局模块。</summary>
        App = 0,
        /// <summary>场景级，主场景切换时会重置，适合当前场景状态。</summary>
        Scene = 1,
        /// <summary>玩法级，适合一局战斗或一个玩法实例的模块。</summary>
        Gameplay = 2,
    }

    /// <summary>
    /// 游戏框架模块抽象类。
    /// <remarks>实现游戏框架具体逻辑。</remarks>
    /// </summary>
    public abstract class Module
    {
        private ModuleSystem.ModuleContext _context;

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public virtual int Priority => 0;

        /// <summary>
        /// 获取模块所属作用域。默认为 <see cref="ModuleScope.App"/>。
        /// </summary>
        /// <remarks>
        /// <see cref="ModuleScope.Scene"/> 作用域的模块会在场景卸载时自动关闭并清理。
        /// </remarks>
        public virtual ModuleScope Scope => ModuleScope.App;

        /// <summary>
        /// 初始化游戏框架模块。
        /// </summary>
        public abstract void OnInit();

        /// <summary>
        /// 关闭并清理游戏框架模块。
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// 获取同一作用域内的其他模块（向上回退到 App 作用域）。
        /// </summary>
        /// <typeparam name="T">模块接口类型。</typeparam>
        /// <returns>模块实例。</returns>
        protected T Require<T>() where T : class => _context.Require<T>();

        /// <summary>
        /// 尝试获取同一作用域内的其他模块。
        /// </summary>
        /// <typeparam name="T">模块接口类型。</typeparam>
        /// <param name="module">获取到的模块实例。</param>
        /// <returns>是否成功获取。</returns>
        protected bool TryGet<T>(out T module) where T : class => _context.TryGet(out module);

        /// <summary>
        /// 注入模块上下文，由 ModuleSystem 在注册时调用。
        /// </summary>
        internal void SetContext(ModuleSystem.ModuleContext context) => _context = context;
    }
}