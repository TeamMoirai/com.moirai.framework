using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 明文存档处理器：容器字节直通存储（无加密）。
    /// </summary>
    /// <remarks>供编辑器调试与可信存储场景使用；上线建议切换 <see cref="AESEncryptedSaveHandler"/>。</remarks>
    [ProviderDisplay(title: "明文存档", description: "容器字节直通存储不加密；仅编辑器调试与可信环境，上线用加密档")]
    [Serializable]
    internal class PlainSaveHandler : SaveServiceHandler
    {
    }
}
