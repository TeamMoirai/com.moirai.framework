using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 明文存档处理器：容器字节直通存储（无加密）。
    /// <para>编辑器调试与可信存储场景使用；上线建议切换 <see cref="AESEncryptedSaveHandler"/>。</para>
    /// </summary>
    [Serializable]
    public class PlainSaveHandler : SaveServiceHandler
    {
        /// <summary>
        /// 载荷变换：容器字节直通（视图别名，零拷贝）。
        /// </summary>
        /// <param name="container">容器字节视图。</param>
        /// <param name="payload">存储载荷视图。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnTransformContainer(SaveBufferSegment container, out SaveBufferSegment payload)
        {
            payload = container;
            return SaveError.None;
        }

        /// <summary>
        /// 载荷还原：存储载荷直通为容器字节（视图别名，零拷贝）。
        /// </summary>
        /// <param name="payload">存储载荷视图。</param>
        /// <param name="container">容器字节视图。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnRestorePayload(SaveBufferSegment payload, out SaveBufferSegment container)
        {
            container = payload;
            return SaveError.None;
        }
    }
}
