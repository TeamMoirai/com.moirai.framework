namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端抽象：数据对象 ↔ 字节的纯序列化通路。
    /// </summary>
    /// <remarks>
    /// 实现必须为纯 .NET 逻辑（工作线程调用，禁止触达 Unity 主线程 API）； <br />
    /// 二进制后端要求目标类型带各自 AOT 标注（<c>MessagePackObject</c>/<c>MemoryPackable</c>/<c>ProtoContract</c>），未标注时序列化器应 fail-fast。 <br />
    /// 实现自述 <see cref="BackendId"/>，容器逐块记录该 2 字节标识；内置与保留 ID 见 <see cref="SaveBackendIds"/>。 <br />
    /// 内置实现经 <see cref="SaveSerializerRegistry"/> 注册；<see cref="SaveServiceSettings.DefaultBackend"/> 为未显式声明后端的数据块提供默认值。 <br />
    /// 进 <see cref="SaveServiceSettings"/> 默认后端下拉的实现按类型全名实例化（<c>Activator</c> 非公共构造可用），无需 <c>[Serializable]</c>； <br />
    /// 但该 ID 必须在 <see cref="SaveSerializerRegistry"/> 有主，否则块写得出、读不回。
    /// </remarks>
    public interface ISaveSerializer
    {
        /// <summary>本序列化器对应的容器后端标识（写入块头 2 字节；0..255 为框架保留区）。</summary>
        ushort BackendId { get; }

        /// <summary>
        /// 将数据对象序列化为字节。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="data">数据对象。</param>
        /// <returns>序列化字节。</returns>
        byte[] Serialize<T>(T data);

        /// <summary>
        /// 从字节反序列化数据对象。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="bytes">序列化字节。</param>
        /// <returns>反序列化后的数据对象。</returns>
        T Deserialize<T>(byte[] bytes);
    }
}
