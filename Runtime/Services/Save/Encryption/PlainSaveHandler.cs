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
    }
}
