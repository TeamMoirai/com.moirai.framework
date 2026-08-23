using System;
using System.IO;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 文件流加密处理器。
    /// </summary>
    [Serializable]
    public sealed class FileStreamEncryptorHandler : ResourceEncryptorHandler
    {
        /// <inheritdoc />
        public override IBundleDecryptor CreateDecryptor() => new FileStreamDecryptor();

        /// <inheritdoc />
        public override IBundleEncryptor CreateEncryptor() => new FileStreamEncryptor();

        #region 资源文件流加解密 [FileStream Encryptor/Decryptor]

        /// <summary>
        /// 文件流加密方式
        /// </summary>
        public class FileStreamEncryptor : IBundleEncryptor
        {
            public BundleEncryptResult Encrypt(BundleEncryptArgs args)
            {
                var fileData = File.ReadAllBytes(args.FilePath);
                for (int i = 0; i < fileData.Length; i++)
                {
                    fileData[i] ^= BundleStream.KEY;
                }

                return new BundleEncryptResult(true, fileData);
            }
        }

        /// <summary>
        /// 资源文件流加载解密类
        /// </summary>
        class FileStreamDecryptor : IBundleStreamDecryptor, IBundleMemoryDecryptor
        {
            /// <summary>
            /// 同步方式获取解密的资源包对象
            /// </summary>
            Stream IBundleStreamDecryptor.CreateDecryptionStream(BundleDecryptArgs args)
            {
                return new BundleStream(args.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            /// <summary>
            /// 异步方式获取解密的资源包对象
            /// </summary>
            int IBundleStreamDecryptor.GetBufferSize(BundleDecryptArgs args)
            {
                return 1024;
            }

            /// <summary>
            /// 后备方式获取解密的资源包
            /// 注意：当正常解密方法失败后，会触发后备加载！
            /// 说明：建议通过LoadFromMemory()方法加载资源包作为保底机制。
            /// </summary>
            byte[] IBundleMemoryDecryptor.GetDecryptedData(BundleDecryptArgs args)
            {
                byte[] fileData = args.FileData ?? File.ReadAllBytes(args.FilePath);
                for (int i = 0; i < fileData.Length; i++)
                {
                    fileData[i] ^= BundleStream.KEY;
                }

                return fileData;
            }
        }

        /// <summary>
        /// 资源文件解密流
        /// </summary>
        internal class BundleStream : FileStream
        {
            public const byte KEY = 64;

            public BundleStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access,
                share)
            {
            }

            public BundleStream(string path, FileMode mode) : base(path, mode)
            {
            }

            public override int Read(byte[] array, int offset, int count)
            {
                var index = base.Read(array, offset, count);
                int end = offset + index;
                for (int i = offset; i < end; i++)
                {
                    array[i] ^= KEY;
                }
                return index;
            }
        }

        #endregion
    }
}