using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档压缩提供方抽象基类（框架插拔件惯例：<see cref="SaveServiceSettings"/> 以 [SerializeReference] + ProviderDropdown 持有实例）。
    /// <para>实现 <see cref="ICompressionProvider"/>；写侧使用设置实例压缩并写入其 <see cref="ProviderId"/>，
    /// 读侧按文件头 ID 经 <see cref="SaveCompressionRegistry"/> 查表解压（自定义提供方须注册后才能读回旧档）。</para>
    /// </summary>
    [Serializable]
    public abstract class SaveCompressionProvider : ICompressionProvider
    {
        /// <summary>
        /// 提供方标识（写入文件头；0 保留为「未压缩」）。
        /// </summary>
        public abstract byte ProviderId { get; }

        /// <summary>
        /// 压缩容器字节。
        /// </summary>
        /// <param name="raw">容器字节视图（缓冲区可能为池化租赁——有效区间以视图为准）。</param>
        /// <returns>压缩字节。</returns>
        public abstract byte[] Compress(SaveBufferSegment raw);

        /// <summary>
        /// 解压为容器字节（数据非法时抛异常，由读侧归一为 <see cref="SaveError.Corrupted"/>）。
        /// </summary>
        /// <param name="packed">压缩字节视图（缓冲区可能为池化租赁/文件大缓冲区别名——有效区间以视图为准）。</param>
        /// <returns>容器字节。</returns>
        public abstract byte[] Decompress(SaveBufferSegment packed);
    }
}
