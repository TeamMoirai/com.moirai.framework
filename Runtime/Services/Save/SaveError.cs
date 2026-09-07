namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档操作错误码。
    /// </summary>
    public enum SaveError
    {
        /// <summary>
        /// 操作成功。
        /// </summary>
        None = 0,

        /// <summary>
        /// 存档处理器未就绪（外观降级路径）。
        /// </summary>
        HandlerNotReady,

        /// <summary>
        /// 参数非法（文件名/文件夹名校验失败）。
        /// </summary>
        InvalidArgument,

        /// <summary>
        /// 存档文件不存在。
        /// </summary>
        FileNotFound,

        /// <summary>
        /// 文件格式非法（魔数不匹配或长度不足，含旧格式存档）。
        /// </summary>
        InvalidFormat,

        /// <summary>
        /// 文件格式版本高于当前运行时支持版本。
        /// </summary>
        UnsupportedVersion,

        /// <summary>
        /// 存档损坏（载荷长度不符或 CRC 校验失败）。
        /// </summary>
        Corrupted,

        /// <summary>
        /// 解密失败（密钥不匹配或密文非法）。
        /// </summary>
        DecryptionFailed,

        /// <summary>
        /// 完整性校验失败（HMAC 校验不通过，密文可能被篡改）。
        /// </summary>
        IntegrityCheckFailed,

        /// <summary>
        /// 反序列化失败（载荷明文无法解析为目标类型）。
        /// </summary>
        SerializationFailed,

        /// <summary>
        /// 磁盘 IO 失败。
        /// </summary>
        IoFailed,
    }
}
