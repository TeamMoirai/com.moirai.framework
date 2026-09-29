namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档密钥提供方契约：产出 AES-256 加密密钥与 HMAC-SHA256 认证密钥（各 32 字节）。
    /// </summary>
    /// <remarks>
    /// 派生策略由实现决定（静态口令 PBKDF2 / 运行时注入口令 / HKDF 按用户派生），加密处理器仅消费密钥材料。 <br />
    /// 实现必须为纯 .NET 逻辑（可在工作线程调用），禁止触达 Unity 主线程 API；派生结果应按参数缓存（PBKDF2 为 10 万迭代级开销）。
    /// </remarks>
    public interface ISaveKeyProvider
    {
        /// <summary>
        /// 获取密钥材料（失败返回错误码，输出为 <c>null</c>）。
        /// </summary>
        /// <param name="encryptionKey">成功时的加密密钥（32 字节）。</param>
        /// <param name="macKey">成功时的认证密钥（32 字节）。</param>
        /// <returns>错误码（<see cref="SaveError.None"/> 或 <see cref="SaveError.InvalidArgument"/> 等）。</returns>
        SaveError TryGetKeyMaterial(out byte[] encryptionKey, out byte[] macKey);
    }
}
