using System;
using System.Security.Cryptography;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// AES 加密存档处理器：容器字节经 <see cref="SaveEncryptor"/>（AES-256-CBC + HMAC，encrypt-then-MAC）变换后存储。
    /// <para>密钥与 PBKDF2 迭代次数在 <see cref="OnInit"/> 从 <see cref="SaveServiceSettings"/> 注入；派生密钥按实例缓存（口令/盐文/迭代变更时失效重派生）。</para>
    /// </summary>
    [Serializable]
    public class AesEncryptedSaveHandler : SaveServiceHandler
    {
        [NonSerialized] private SaveEncryptor _encryptor;

        /// <summary>
        /// AES 加密器（懒加载）。
        /// </summary>
        private SaveEncryptor Encryptor => _encryptor ??= new SaveEncryptor();

        /// <summary>
        /// 保存和加载文件的密钥（代理至加密器；未注入时为占位默认值）。
        /// <para>与用户/设备绑定的进阶密钥策略（如按平台账号派生）由游戏层自行覆盖。</para>
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
            Encryptor.Key = SaveServiceSettings.EncryptionKey;
            Encryptor.Iterations = SaveServiceSettings.Pbkdf2Iterations;
        }

        /// <summary>
        /// 载荷变换：容器字节加密为存储载荷。
        /// </summary>
        /// <param name="container">容器字节。</param>
        /// <param name="payload">存储载荷字节。</param>
        /// <returns>错误码。</returns>
        protected internal sealed override SaveError OnTransformContainer(byte[] container, out byte[] payload)
        {
            return Encryptor.TryEncrypt(container, Encryptor.Key, out payload);
        }

        /// <summary>
        /// 载荷还原：存储载荷验证并解密为容器字节。
        /// </summary>
        /// <param name="payload">存储载荷字节。</param>
        /// <param name="container">容器字节。</param>
        /// <returns>错误码（<see cref="SaveError.IntegrityCheckFailed"/> = 疑似篡改；<see cref="SaveError.DecryptionFailed"/> = 密钥不匹配/密文非法）。</returns>
        protected internal sealed override SaveError OnRestorePayload(byte[] payload, out byte[] container)
        {
            return Encryptor.TryDecrypt(payload, Encryptor.Key, out container);
        }
    }
}
