#if SERILOG_INSTALLED
using Serilog.Core;
using Serilog.Events;

namespace Moirai.Atropos.Serilog
{
    /// <summary>
    /// 将 Unity 上下文对象（<see cref="UnityEngine.Object"/>）写入日志事件属性的增强器。
    /// <para>经 <see cref="LoggerExtensions.ForContext"/> 附加上下文后，<see cref="Unity3DLogEventSink"/> 输出时 Unity Console 可点击日志定位到该对象。</para>
    /// </summary>
    public sealed class UnityObjectEnricher : ILogEventEnricher
    {
        /// <summary>
        /// 上下文属性的保留键名（刻意使用特殊命名避免与用户属性冲突，仅供 <see cref="Unity3DLogEventSink"/> 内部消费）。
        /// </summary>
        public const string UNITY_CONTEXT_KEY = "%_DO_NOT_USE_UNITY_ID_DO_NOT_USE%";

        /// <summary>
        /// 预构建的上下文属性（不可变，可在所有日志事件间复用）。
        /// </summary>
        private readonly LogEventProperty _property;

        /// <summary>
        /// 创建 Unity 上下文对象增强器。
        /// </summary>
        /// <param name="context">要附加到每条日志事件的 Unity 上下文对象。</param>
        public UnityObjectEnricher(UnityEngine.Object context) =>
            _property = new LogEventProperty(UNITY_CONTEXT_KEY, new ScalarValue(context));

        /// <summary>
        /// 增强日志事件：若事件尚未携带上下文属性，则附加上下文对象。
        /// </summary>
        /// <param name="logEvent">要增强的日志事件。</param>
        /// <param name="propertyFactory">属性工厂（本实现使用预构建属性，不使用该参数）。</param>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
            logEvent.AddPropertyIfAbsent(_property);
    }
}
#endif
