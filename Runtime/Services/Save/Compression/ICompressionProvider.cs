namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档压缩提供方契约：容器字节与压缩字节之间的双向变换（压缩在加密前、解压在解密后，顺序固定）。
    /// <para>每个提供方声明唯一 <see cref="ProviderId"/> 写入文件头（offset 24-27），读侧按 ID 经
    /// <see cref="SaveCompressionRegistry"/> 查表还原——未知 ID 判别为 <see cref="SaveError.UnsupportedVersion"/>（未来格式保护）。</para>
    /// <para>实现必须为无状态纯 .NET 逻辑（任意线程并发调用安全）。</para>
    /// </summary>
    public interface ICompressionProvider
    {
        /// <summary>
        /// 提供方标识（写入文件头；0 保留为「未压缩」，注册表拒绝登记）。
        /// </summary>
        byte ProviderId { get; }

        /// <summary>
        /// 压缩容器字节。
        /// </summary>
        /// <param name="raw">容器字节视图（缓冲区可能为池化租赁——有效区间以视图为准）。</param>
        /// <returns>压缩字节。</returns>
        byte[] Compress(SaveBufferSegment raw);

        /// <summary>
        /// 解压为容器字节（数据非法时抛异常，由读侧归一为 <see cref="SaveError.Corrupted"/>）。
        /// </summary>
        /// <param name="packed">压缩字节视图（缓冲区可能为池化租赁/文件大缓冲区别名——有效区间以视图为准）。</param>
        /// <returns>容器字节。</returns>
        byte[] Decompress(SaveBufferSegment packed);
    }
}
