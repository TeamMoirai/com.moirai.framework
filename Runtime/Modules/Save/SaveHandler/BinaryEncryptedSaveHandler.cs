using System.IO;
using System;
using System.Runtime.Serialization.Formatters.Binary;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 二进制格式加密存档后端配置。
    /// </summary>
    [Serializable]
    public sealed class BinaryEncryptedSaveHandlerConfig : SaveServiceHandlerConfig
    {
        /// <inheritdoc />
        public override bool IsEncrypted => true;

        /// <inheritdoc />
        public override SaveServiceHandler CreateHandler()
        {
            return new BinaryEncryptedSaveHandler();
        }
    }

    /// <summary>
    /// 此保存加载方法将文件保存并加载为加密的二进制文件
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: BinaryFormatter is vulnerable to deserialization attacks (RCE).
    /// It has been deprecated by Microsoft and is removed in .NET 9+.
    /// Consider migrating to JsonEncryptedSaveHandler for new projects.
    /// See: https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide
    /// </remarks>
    [System.Obsolete("BinaryFormatter is insecure and deprecated. Use JsonEncryptedSaveHandler instead. See https://aka.ms/binaryformatter")]
    public class BinaryEncryptedSaveHandler : EncryptedSaveHandlerBase
    {
        private BinaryFormatter _formatter;

        private BinaryFormatter Formatter => _formatter ??= new BinaryFormatter();

        protected override void SerializeToStream(object objectToSave, MemoryStream stream)
        {
            Formatter.Serialize(stream, objectToSave);
        }

        protected override T DeserializeFromStream<T>(MemoryStream stream)
        {
            return (T)Formatter.Deserialize(stream);
        }
    }
}
