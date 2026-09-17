using System.IO;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档压缩提供方契约：容器字节流与压缩字节流之间的双向流式变换（压缩在加密前、解压在解密后，顺序固定）。
    /// <para>每个提供方声明唯一 <see cref="ProviderId"/> 写入文件头（offset 24-27），读侧按 ID 经
    /// <see cref="SaveCompressionRegistry"/> 查表还原——未知 ID 判别为 <see cref="SaveError.UnsupportedVersion"/>（未来格式保护）。</para>
    /// <para>流式契约：写侧经 <see cref="OpenCompressStream"/> 包装流灌入容器段流（零整档容器缓冲）；
    /// 读侧经 <see cref="OpenDecompressStream"/> 包装流逐段产出（零整档解压输出驻留——段池拉取见读管线）。</para>
    /// <para>实现必须为无状态纯 .NET 逻辑（任意线程并发调用安全）。</para>
    /// </summary>
    public interface ICompressionProvider
    {
        /// <summary>
        /// 提供方标识（写入文件头；0 保留为「未压缩」，注册表拒绝登记）。
        /// </summary>
        byte ProviderId { get; }

        /// <summary>
        /// 打开压缩包装流：容器字节经返回流写入即压缩落底层目标流（调用方负责关闭返回流以收尾压缩帧；
        /// 返回流关闭不得关闭底层目标流）。
        /// </summary>
        /// <param name="target">压缩字节的落点流（生命周期由调用方管理）。</param>
        /// <returns>压缩包装流（只写）。</returns>
        Stream OpenCompressStream(Stream target);

        /// <summary>
        /// 打开解压包装流：自底层源流读取压缩数据并逐段产出容器字节（调用方负责关闭返回流；
        /// 数据非法时读取/关闭抛异常，由读侧归一为 <see cref="SaveError.Corrupted"/>；返回流关闭不得关闭底层源流）。
        /// </summary>
        /// <param name="source">压缩字节源流（生命周期由调用方管理）。</param>
        /// <returns>解压包装流（只读）。</returns>
        Stream OpenDecompressStream(Stream source);
    }
}
