#if SERILOG_INSTALLED
using Serilog.Core;
using Serilog.Events;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// 将 Unity 标签写入日志事件属性的增强器。
    /// <para>经 <see cref="LoggerExtensions.WithUnityTag"/> 附加标签后，<see cref="Unity3DLogEventSink"/> 会将其作为 Unity 日志标签输出。</para>
    /// </summary>
    public sealed class UnityTagEnricher : ILogEventEnricher
    {
        /// <summary>
        /// 标签属性的保留键名（刻意使用特殊命名避免与用户属性冲突，仅供 <see cref="Unity3DLogEventSink"/> 内部消费）。
        /// </summary>
        public const string UNITY_TAG_KEY = "%_DO_NOT_USE_UNITY_TAG_DO_NOT_USE%";

        /// <summary>
        /// 预构建的标签属性（不可变，可在所有日志事件间复用）。
        /// </summary>
        private readonly LogEventProperty _property;

        /// <summary>
        /// 创建 Unity 标签增强器。
        /// </summary>
        /// <param name="tag">要附加到每条日志事件的 Unity 标签。</param>
        public UnityTagEnricher(string tag) =>
            _property = new LogEventProperty(UNITY_TAG_KEY, new ScalarValue(tag));

        /// <summary>
        /// 增强日志事件：若事件尚未携带标签属性，则附加标签。
        /// </summary>
        /// <param name="logEvent">要增强的日志事件。</param>
        /// <param name="propertyFactory">属性工厂（本实现使用预构建属性，不使用该参数）。</param>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
            logEvent.AddPropertyIfAbsent(_property);
    }
}
#endif
