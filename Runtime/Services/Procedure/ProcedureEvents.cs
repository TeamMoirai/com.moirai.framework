namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程域事件标记。
    /// <para>标记归属于游戏流程（启动链、热更、入口切换）上下文的事件类型，供约定检索与诊断使用；
    /// 框架不据此过滤分发，仅作为领域契约锚点。</para>
    /// <para>流程状态机自身的切换广播不走该标记，见 <see cref="ProcedureService.onProcedureChanged"/>。</para>
    /// </summary>
    public interface IProcedureEvent { }

    /// <summary>
    /// 流程切换种类。
    /// </summary>
    public enum ProcedureTransitionKind
    {
        /// <summary>启动首个流程（<see cref="ProcedureTransitionRecord.From"/> 为 null）。</summary>
        Start,

        /// <summary>运行中切换流程。</summary>
        Change,

        /// <summary>状态机关停（<see cref="ProcedureTransitionRecord.To"/> 为 null，仅记入历史，不广播）。</summary>
        Shutdown,
    }

    /// <summary>
    /// 流程切换记录（值类型快照）。
    /// <para>既作为 <see cref="ProcedureService.onProcedureChanged"/> 的广播载荷，
    /// 也作为 <see cref="ProcedureServiceHandler.TransitionHistory"/> 的历史条目，均不可变。</para>
    /// </summary>
    public readonly struct ProcedureTransitionRecord
    {
        /// <summary>切换种类。</summary>
        public ProcedureTransitionKind Kind { get; }

        /// <summary>切出的流程（启动切换时为 null）。</summary>
        public ProcedureBase From { get; }

        /// <summary>切入的流程（关停切换时为 null）。</summary>
        public ProcedureBase To { get; }

        /// <summary>切出流程的驻留时长（秒）。</summary>
        public float FromElapsed { get; }

        /// <summary>
        /// 创建切换记录。
        /// </summary>
        public ProcedureTransitionRecord(ProcedureTransitionKind kind, ProcedureBase from, ProcedureBase to, float fromElapsed)
        {
            Kind = kind;
            From = from;
            To = to;
            FromElapsed = fromElapsed;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string fromName = From != null ? From.GetType().Name : "(启动)";
            string toName = To != null ? To.GetType().Name : "(关停)";
            return StringUtility.Format("[{0}] {1} -> {2} ({3:F2}s)", Kind, fromName, toName, FromElapsed);
        }
    }
}
