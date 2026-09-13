namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端标识。
    /// <para>容器内逐块记录后端标识（<see cref="SaveBlockInfo.Backend"/>），读取时按块还原——不同数据块可在同一存档文件内混用不同后端。</para>
    /// <para>二进制后端为项目级硬依赖（NuGet 引入）；未接入时 <see cref="SaveSerializerRegistry.GetRequired"/> fail-fast。</para>
    /// </summary>
    public enum ESaveBackend
    {
        /// <summary>
        /// 框架内置 JSON 序列化（零分配字节通路，默认后端）。
        /// </summary>
        Json = 0,

        /// <summary>
        /// MessagePack 二进制序列化（Schema-less，需 <see cref="MessagePack.MessagePackObjectAttribute"/> 类标注 + SourceGenerator）。
        /// </summary>
        MessagePack = 1,

        /// <summary>
        /// MemoryPack 二进制序列化（零编码开销，需 <see cref="MemoryPack.MemoryPackableAttribute"/> 类标注 + SourceGenerator）。
        /// </summary>
        MemoryPack = 2,

        /// <summary>
        /// protobuf-net 二进制序列化（Proto 契约，需 <see cref="ProtoBuf.ProtoContractAttribute"/> 类标注 + BuildTools SourceGenerator）。
        /// </summary>
        Protobuf = 3,

        /// <summary>
        /// 框架内置键值捕获格式（无代码保存组件专用；块内为字段级键值记录，天然容忍字段增删）。
        /// </summary>
        KeyValue = 254,
    }
}
