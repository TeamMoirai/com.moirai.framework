using System;
using System.IO;
using System.Security.Cryptography;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 加密存档处理器基类：提供统一的加密/解密工作流与错误处理。
    /// <para>加密能力通过组合 <see cref="SaveEncryptor"/> 获得（C# 不支持多基类）；
    /// 密钥与 PBKDF2 迭代次数在 <see cref="OnInit"/> 从 <see cref="SaveServiceSettings"/> 注入。</para>
    /// 子类只需实现 <see cref="SerializeToStream"/> / <see cref="DeserializeFromStream{T}"/>
    /// 具体格式的序列化逻辑（明文侧，字节通路）。
    /// </summary>
    [Serializable]
    public abstract class EncryptedSaveHandlerBase : SaveServiceHandler
    {
        [NonSerialized] private SaveEncryptor _encryptor;

        /// <summary>
        /// AES 加密器（懒加载）。
        /// </summary>
        private SaveEncryptor Encryptor => _encryptor ??= new SaveEncryptor();

        /// <summary>
        /// 保存和加载文件的密钥（代理至加密器；未注入时为占位默认值）。
        /// </summary>
        protected internal string Key
        {
            get => Encryptor.Key;
            set => Encryptor.Key = value;
        }

        /// <summary>
        /// 初始化时从 <see cref="SaveServiceSettings"/> 注入加密密钥与 PBKDF2 迭代次数。
        /// </summary>
        protected override void OnInit()
        {
            Key = SaveServiceSettings.EncryptionKey;
            Encryptor.Iterations = SaveServiceSettings.Pbkdf2Iterations;
        }

        /// <summary>
        /// 序列化：明文序列化 → 加密为载荷字节。
        /// </summary>
        /// <param name="saveObject">存档对象。</param>
        /// <returns>加密后的载荷字节。</returns>
        protected internal sealed override byte[] Serialize(object saveObject)
        {
            using (MemoryStream plaintextStream = new MemoryStream())
            {
                SerializeToStream(saveObject, plaintextStream);
                SaveError error = Encryptor.TryEncrypt(plaintextStream.ToArray(), Encryptor.Key, out byte[] encrypted);
                if (error != SaveError.None)
                {
                    throw new CryptographicException(StringUtility.Format("Save encryption failed with error '{0}'.", error));
                }

                return encrypted;
            }
        }

        /// <summary>
        /// 反序列化：载荷字节验证并解密 → 明文反序列化。
        /// <para>失败抛出 <see cref="SaveOperationException"/>（分型错误码）或 <see cref="CryptographicException"/>，由文件管线统一兜底。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="payload">加密载荷字节。</param>
        /// <returns>反序列化后的对象。</returns>
        protected internal sealed override T Deserialize<T>(byte[] payload)
        {
            SaveError error = Encryptor.TryDecrypt(payload, Encryptor.Key, out byte[] plaintext);
            if (error != SaveError.None)
            {
                throw new SaveOperationException(error, StringUtility.Format("Save decryption failed with error '{0}'.", error));
            }

            using (MemoryStream plaintextStream = new MemoryStream(plaintext))
            {
                return DeserializeFromStream<T>(plaintextStream);
            }
        }

        /// <summary>
        /// 将存档对象序列化到明文流。由子类实现具体格式。
        /// </summary>
        /// <param name="objectToSave">存档对象。</param>
        /// <param name="stream">明文输出流。</param>
        protected abstract void SerializeToStream(object objectToSave, MemoryStream stream);

        /// <summary>
        /// 从明文流反序列化对象。由子类实现具体格式。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="stream">明文输入流。</param>
        /// <returns>反序列化后的对象。</returns>
        protected abstract T DeserializeFromStream<T>(MemoryStream stream);
    }
}
