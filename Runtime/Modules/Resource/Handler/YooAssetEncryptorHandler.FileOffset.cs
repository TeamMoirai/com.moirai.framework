using System;
using System.IO;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 文件偏移加密处理器。
    /// </summary>
    [Serializable]
    public sealed class FileOffsetEncryptorHandler : YooAssetEncryptorHandler
    {
        internal static int GetFileOffset()
        {
            return 32;
        }

        /// <inheritdoc />
        public override IBundleDecryptor CreateDecryptor() => new FileOffsetDecryptor();

        /// <inheritdoc />
        public override IBundleEncryptor CreateEncryptor() => new FileOffsetEncryptor();

        #region 文件偏移加解密 [FileOffset Encryptor/Decryptor]

        /// <summary>
        /// 文件偏移加密方式
        /// </summary>
        public class FileOffsetEncryptor : IBundleEncryptor
        {
            public BundleEncryptResult Encrypt(BundleEncryptArgs args)
            {
                int offset = GetFileOffset();
                byte[] fileData = File.ReadAllBytes(args.FilePath);
                var encryptedData = new byte[fileData.Length + offset];
                Buffer.BlockCopy(fileData, 0, encryptedData, offset, fileData.Length);
                return new BundleEncryptResult(true, encryptedData);
            }
        }

        /// <summary>
        /// 资源文件偏移加载解密类
        /// </summary>
        internal class FileOffsetDecryptor : IBundleOffsetDecryptor, IBundleMemoryDecryptor
        {
            /// <summary>
            /// 同步方式获取解密的资源包对象
            /// 注意：加载流对象在资源包对象释放的时候会自动释放
            /// </summary>
            long IBundleOffsetDecryptor.GetFileOffset(BundleDecryptArgs args)
            {
                return (long)GetFileOffset();
            }

            /// <summary>
            /// 异步方式获取解密的资源包对象
            /// 注意：加载流对象在资源包对象释放的时候会自动释放
            /// </summary>
            byte[] IBundleMemoryDecryptor.GetDecryptedData(BundleDecryptArgs args)
            {
                byte[] fileData = args.FileData ?? File.ReadAllBytes(args.FilePath);
                int fileOffset = GetFileOffset();
                if (fileData.Length <= fileOffset)
                {
                    return Array.Empty<byte>();
                }

                int outputLength = fileData.Length - fileOffset;
                byte[] output = new byte[outputLength];
                Buffer.BlockCopy(fileData, fileOffset, output, 0, outputLength);
                return output;
            }
        }

        #endregion
    }
}