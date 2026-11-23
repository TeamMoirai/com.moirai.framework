namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 资源门面能力契约（internal）：门面需要后端提供的内部能力面——后端初始化（随契约瘦身移出 <see cref="IAssetService"/> 的成员）。
    /// </summary>
    /// <remarks>
    /// 替代家族基座强转：门面经 <c>is IAssetServiceBackend</c> 能力协商取用，不再依赖 <see cref="AssetServiceHandler"/> 谱系。 <br />
    /// 成员为 public 无参签名，实现类可隐式实现；内建路径：派生 <see cref="AssetServiceHandler"/>（自动获得能力）。 <br />
    /// 范式与 Save 侧 <see cref="Moirai.Atropos.Save.ISaveServiceBackend"/> 一致：门面能力 = internal 能力契约。
    /// </remarks>
    internal interface IAssetServiceBackend
    {
        /// <summary>同步初始化后端（创建默认包与绑定服务；由门面编排钩子经能力协商调用）。</summary>
        void Initialize();
    }
}
