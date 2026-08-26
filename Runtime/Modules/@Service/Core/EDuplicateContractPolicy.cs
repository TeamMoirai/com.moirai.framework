namespace Moirai.Atropos
{
    /// <summary>
    /// 重复契约注册处置策略。仅作用于"同作用域内已占用契约再次显式注册不同实例"的场景。
    /// <para>同实例重复注册始终幂等返回既有实例；依赖链自动预注册的去重始终静默——两者均不受本策略影响。</para>
    /// </summary>
    public enum EDuplicateContractPolicy : byte
    {
        /// <summary>
        /// 静默丢弃新实例并返回既有实例。发布构建默认值（零运行时成本）。
        /// </summary>
        Skip = 0,

        /// <summary>
        /// 记录警告后丢弃新实例并返回既有实例。编辑器与开发构建默认值——意外抢占契约不再静默。
        /// </summary>
        Warn = 1,

        /// <summary>
        /// 抛出 <see cref="GameException"/>（fail-fast）。适用于强约束的集成验证与问题排查期。
        /// </summary>
        Throw = 2,
    }
}
