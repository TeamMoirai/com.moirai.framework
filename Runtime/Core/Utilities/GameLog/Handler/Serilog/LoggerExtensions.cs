#if SERILOG_INSTALLED
using Serilog;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// Serilog 日志器扩展：为日志附加 Unity 上下文对象与标签。
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// 为日志添加 <see cref="UnityEngine.Object"/> 上下文。
        /// </summary>
        /// <param name="logger">原始日志器。</param>
        /// <param name="context">日志的 <see cref="UnityEngine.Object"/> 上下文。</param>
        public static ILogger ForContext(this ILogger logger, UnityEngine.Object context) => logger.ForContext(new UnityObjectEnricher(context));

        /// <summary>
        /// 为日志添加 Unity 标签。
        /// </summary>
        /// <param name="logger">原始日志器。</param>
        /// <param name="tag">Unity 日志标签。</param>
        public static ILogger WithUnityTag(this ILogger logger, string tag) => logger.ForContext(new UnityTagEnricher(tag));
    }
}
#endif
