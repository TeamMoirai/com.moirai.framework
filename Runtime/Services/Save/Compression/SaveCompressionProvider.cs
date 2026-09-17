using System;
using System.IO;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档压缩提供方抽象基类（框架插拔件惯例：<see cref="SaveServiceSettings"/> 以 [SerializeReference] + ProviderDropdown 持有实例）。
    /// <para>实现 <see cref="ICompressionProvider"/> 流式契约；写侧使用设置实例压缩并写入其 <see cref="ProviderId"/>，
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
        /// 打开压缩包装流：容器字节经返回流写入即压缩落底层目标流（调用方负责关闭返回流以收尾压缩帧；
        /// 返回流关闭不得关闭底层目标流）。
        /// </summary>
        /// <param name="target">压缩字节的落点流（生命周期由调用方管理）。</param>
        /// <returns>压缩包装流（只写）。</returns>
        public abstract Stream OpenCompressStream(Stream target);

        /// <summary>
        /// 打开解压包装流：自底层源流读取压缩数据并逐段产出容器字节（数据非法时读取/关闭抛异常；
        /// 返回流关闭不得关闭底层源流）。
        /// </summary>
        /// <param name="source">压缩字节源流（生命周期由调用方管理）。</param>
        /// <returns>解压包装流（只读）。</returns>
        public abstract Stream OpenDecompressStream(Stream source);
    }
}
