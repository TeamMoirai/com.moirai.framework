using System.Collections.Generic;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 压缩提供方注册表：文件头 <c>CompressionProviderId</c> → <see cref="ICompressionProvider"/> 实例。
    /// <para>内建注册 GZip（ID 1）；自定义提供方（如可选 LZ4 包）经 <see cref="Register"/> 登记后才能读回其写出的旧档。
    /// ID 0 保留为「未压缩」、重复 ID 登记 fail-fast——注册表是读侧格式还原的唯一事实源，撞 ID 等于静默写坏档。</para>
    /// <para>注册/注销为非热路径加锁，读侧查表允许并发（字典引用整体替换外的并发读在注册期外成立——注册集中在初始化期完成）。</para>
    /// </summary>
    public static class SaveCompressionRegistry
    {
        /// <summary>提供方表（ID → 实例；受 <see cref="s_Lock"/> 保护）。</summary>
        private static readonly Dictionary<byte, ICompressionProvider> s_Providers = new Dictionary<byte, ICompressionProvider>();

        /// <summary>注册表锁（注册/注销极快，非热路径，遵守并发安全 lock 方案）。</summary>
        private static readonly object s_Lock = new object();

        static SaveCompressionRegistry()
        {
            Register(GZipCompressionProvider.Shared);
        }

        /// <summary>
        /// 注册压缩提供方（ID 0 或重复 ID 登记 fail-fast）。
        /// </summary>
        /// <param name="provider">提供方实例。</param>
        public static void Register(ICompressionProvider provider)
        {
            if (provider == null)
            {
                throw new GameException("Compression provider is null.");
            }

            if (provider.ProviderId == 0)
            {
                throw new GameException(StringUtility.Format("Compression provider '{0}' uses reserved id 0 (uncompressed marker).", provider.GetType().FullName));
            }

            lock (s_Lock)
            {
                if (s_Providers.TryGetValue(provider.ProviderId, out ICompressionProvider existing))
                {
                    throw new GameException(StringUtility.Format("Compression provider id {0} is already registered to '{1}', cannot register '{2}'.",
                        provider.ProviderId, existing.GetType().FullName, provider.GetType().FullName));
                }

                s_Providers.Add(provider.ProviderId, provider);
            }
        }

        /// <summary>
        /// 按 ID 查询压缩提供方。
        /// </summary>
        /// <param name="providerId">提供方标识（文件头读取）。</param>
        /// <param name="provider">命中时的提供方实例。</param>
        /// <returns>已注册返回 <c>true</c>。</returns>
        public static bool TryGet(byte providerId, out ICompressionProvider provider)
        {
            lock (s_Lock)
            {
                return s_Providers.TryGetValue(providerId, out provider);
            }
        }

        /// <summary>
        /// 注销压缩提供方（测试/热卸载用；内建提供方注销后其旧档将按未知 ID 拒载）。
        /// </summary>
        /// <param name="providerId">提供方标识。</param>
        /// <returns>移除成功返回 <c>true</c>。</returns>
        internal static bool Unregister(byte providerId)
        {
            lock (s_Lock)
            {
                return s_Providers.Remove(providerId);
            }
        }
    }
}
