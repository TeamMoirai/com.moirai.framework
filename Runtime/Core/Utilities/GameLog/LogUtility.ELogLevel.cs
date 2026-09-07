namespace Moirai.Atropos
{
    public static partial class LogUtility
    {
        /// <summary>
        /// 游戏框架日志等级。
        /// </summary>
        public enum ELogLevel : byte
        {
            /// <summary>
            /// 最详细的日志，可能包含敏感数据（如参数值、令牌等）
            /// </summary>
            /// <example>仅在开发环境临时调试，或生产环境排查极难复现的问题时临时开启</example>
            /// <remarks>默认关闭，绝不能在生产环境长期启用，否则有安全风险</remarks>
            Verbose = 0,

            /// <summary>
            /// 用于交互式调试，记录变量状态、方法进入/退出等
            /// </summary>
            /// <example>开发人员在本地开发时使用，辅助定位逻辑错误</example>
            /// <remarks>无长期保留价值，生产环境通常关闭（除非临时调试）</remarks>
            Debug,

            /// <summary>
            /// 记录应用程序的常规流程，如服务启动、用户登录、业务操作完成
            /// </summary>
            /// <example>生产环境推荐的最小日志级别，用于跟踪系统运行状态和审计</example>
            /// <remarks>应具有长期保留价值，帮助运维人员了解系统行为</remarks>
            Info,

            /// <summary>
            /// 发生异常或意外情况，但不影响当前操作继续执行（如降级处理、重试成功）
            /// </summary>
            /// <example>记录非致命问题，便于提前预警，如磁盘空间不足、请求稍慢</example>
            /// <remarks>表明系统仍可用，但需关注潜在风险</remarks>
            Warning,

            /// <summary>
            /// 当前请求/活动因失败而中断，但不影响整个应用进程（如 HTTP 500、数据库连接失败）
            /// </summary>
            /// <example>记录业务错误或组件故障，需开发人员介入排查</example>
            /// <remarks>这是最常见的“报错”级别，重点监控</remarks>
            Error,

            /// <summary>
            /// 灾难性故障，应用或系统崩溃、数据丢失风险，需立即人工干预
            /// </summary>
            /// <example>如无法恢复的异常、关键服务宕机、数据库不可用</example>
            /// <remarks>应当触发警报，通知值班人员</remarks>
            Fatal
        }
    }
}