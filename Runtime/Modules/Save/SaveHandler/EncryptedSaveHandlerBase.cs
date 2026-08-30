using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 加密存档处理器基类：提供统一的加密/解密工作流与错误处理。
    /// <para>加密能力通过组合 <see cref="SaveEncryptor"/> 获得（C# 不支持多基类）。</para>
    /// 子类只需实现 <see cref="SerializeToStream"/> / <see cref="DeserializeFromStream{T}"/>
    /// 具体格式的序列化逻辑。
    /// </summary>
    public abstract class EncryptedSaveHandlerBase : SaveServiceHandler
    {
        private SaveEncryptor _encryptor;

        /// <summary>
        /// AES 加密器（懒加载）。
        /// </summary>
        private SaveEncryptor Encryptor => _encryptor ??= new SaveEncryptor();

        /// <summary>
        /// 保存和加载文件的密钥。
        /// </summary>
        protected string Key
        {
            get => Encryptor.Key;
            set => Encryptor.Key = value;
        }

        /// <summary>
        /// 初始化时从 <see cref="SaveServiceSettings"/> 注入加密密钥。
        /// </summary>
        protected override void OnInit()
        {
            Key = SaveServiceSettings.EncryptionKey;
        }

        protected internal override UniTask SerializeAsync(object objectToSave, FileStream saveFile)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                SerializeToStream(objectToSave, memoryStream);
                memoryStream.Position = 0;
                Encryptor.Encrypt(memoryStream, saveFile, Encryptor.Key);
            }
            saveFile.Flush();
            saveFile.Close();

            return UniTask.CompletedTask;
        }

        protected internal override UniTask<T> DeserializeAsync<T>(FileStream saveFile)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                try
                {
                    Encryptor.Decrypt(saveFile, memoryStream, Encryptor.Key);
                }
                catch (CryptographicException ce)
                {
                    LogUtility.Error("[SaveServiceHandler] Decryption failed: " + ce);
                    return UniTask.FromResult<T>(default);
                }
                memoryStream.Position = 0;
                T savedObject = DeserializeFromStream<T>(memoryStream);
                saveFile.Close();
                return UniTask.FromResult(savedObject);
            }
        }

        /// <summary>
        /// Serialize the object into the provided stream.
        /// </summary>
        protected abstract void SerializeToStream(object objectToSave, MemoryStream stream);

        /// <summary>
        /// Deserialize an object of type T from the provided stream.
        /// </summary>
        protected abstract T DeserializeFromStream<T>(MemoryStream stream);
    }
}
