namespace Moirai.Atropos
{
    /// <summary>
    /// 框架内置服务轮询优先级常量。全部内置服务的 <see cref="IService.Priority"/> 统一压在 -1000 及以下，
    /// 与业务服务（默认 0 及以上）分带，便于优先处理内置服务。
    /// <para>轮询按 Priority 降序（数值越大越先 Tick）。</para>
    /// </summary>
    public static class ServicePriorityOrder
    {
        /// <summary>GameObject 池服务（内置带最高，最先轮询）。</summary>
        public const int GAME_OBJECT_POOL = -1000;

        /// <summary>通用对象池服务。</summary>
        public const int OBJECT_POOL = -1001;

        /// <summary>资源服务。</summary>
        public const int RESOURCE = -1002;

        /// <summary>
        /// 中层内置服务默认优先级（音频/UI/计时器/场景/本地化/输入/配置表/存档）。
        /// 同值时按注册先后轮询。
        /// </summary>
        public const int MID_TIER = -1005;

        /// <summary>调试器服务。</summary>
        public const int DEBUGGER = -1009;

        /// <summary>流程服务（内置带最低，最后轮询）。</summary>
        public const int PROCEDURE = -1010;
    }
}
