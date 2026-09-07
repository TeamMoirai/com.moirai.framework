using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 明文存档处理器：容器字节直通存储（无加密）。
    /// <para>编辑器调试与可信存储场景使用；上线建议切换 <see cref="AesEncryptedSaveHandler"/>。</para>
    /// </summary>
    [Serializable]
    public class PlainSaveHandler : SaveServiceHandler
    {
        /// <summary>
        /// 载荷变换：容器字节直通。
        /// </summary>
        /// <param name="container">容器字节。</param>
        /// <param name="payload">存储载荷字节。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnTransformContainer(byte[] container, out byte[] payload)
        {
            payload = container;
            return SaveError.None;
        }

        /// <summary>
        /// 载荷还原：存储载荷直通为容器字节。
        /// </summary>
        /// <param name="payload">存储载荷字节。</param>
        /// <param name="container">容器字节。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnRestorePayload(byte[] payload, out byte[] container)
        {
            container = payload;
            return SaveError.None;
        }
    }
}
