using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 此类实现用于加密和解密流的抽象方法
    /// </summary>
    public abstract class SaveEncryptor
    {
        /// <summary>
        /// 保存和加载文件的密钥。
        /// SECURITY: Must be overridden with a unique, per-project secret before shipping.
        /// </summary>
        public virtual string Key { get; set; } = "CHANGE_ME_BEFORE_SHIPPING";

        /// <summary>
        /// 加密盐文。
        /// SECURITY: Must be overridden with a unique, per-project value before shipping.
        /// </summary>
        public virtual string Salt { get; set; } = "CHANGE_ME_SALT";

        /// <summary>
        /// 使用参数中传入的密钥将指定的输入流加密到指定的输出流中
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="sKey"></param>
        protected virtual void Encrypt(Stream inputStream, Stream outputStream, string sKey)
        {
            using var algorithm = Aes.Create();
            Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(sKey, Encoding.ASCII.GetBytes(Salt));

            algorithm.Key = key.GetBytes(algorithm.KeySize / 8);
            algorithm.IV = key.GetBytes(algorithm.BlockSize / 8);

            CryptoStream cryptoStream = new CryptoStream(inputStream, algorithm.CreateEncryptor(), CryptoStreamMode.Read);
            cryptoStream.CopyTo(outputStream);
        }

        /// <summary>
        /// 使用参数中传入的密钥将输入流解密为输出流
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="sKey"></param>
        protected virtual void Decrypt(Stream inputStream, Stream outputStream, string sKey)
        {
            using var algorithm = Aes.Create();
            Rfc2898DeriveBytes key = new Rfc2898DeriveBytes(sKey, Encoding.ASCII.GetBytes(Salt));

            algorithm.Key = key.GetBytes(algorithm.KeySize / 8);
            algorithm.IV = key.GetBytes(algorithm.BlockSize / 8);

            CryptoStream cryptoStream = new CryptoStream(inputStream, algorithm.CreateDecryptor(), CryptoStreamMode.Read);
            cryptoStream.CopyTo(outputStream);
        }
    }
}
