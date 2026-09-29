#if SERILOG_INSTALLED
using Serilog;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// Serilog 日志器扩展：为日志附加 Unity 上下文对象与标签。
    /// </summary>
    internal static class LoggerExtensions
    {
        /// <summary>
        /// 为日志添加 <see cref="UnityEngine.Object"/> 上下文，使 Unity Console 点击日志可定位到该对象。
        /// </summary>
        /// <remarks>刻意不命名为 <c>ForContext</c>：Serilog 的 <c>ILogger.ForContext&lt;TProperty&gt;</c> 实例重载在解析中优先，同名扩展方法会变成静默失效的死路径。 <br />
        /// </remarks>
        /// <param name="logger">原始日志器。</param>
        /// <param name="context">日志的 <see cref="UnityEngine.Object"/> 上下文。</param>
        public static ILogger WithUnityObject(this ILogger logger, UnityEngine.Object context) => logger.ForContext(new UnityObjectEnricher(context));

        /// <summary>
        /// 为日志添加 Unity 标签。
        /// </summary>
        /// <param name="logger">原始日志器。</param>
        /// <param name="tag">Unity 日志标签。</param>
        public static ILogger WithUnityTag(this ILogger logger, string tag) => logger.ForContext(new UnityTagEnricher(tag));
    }
}
#endif
