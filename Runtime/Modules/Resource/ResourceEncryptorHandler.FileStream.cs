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
        /// <remarks>仅实现流式解密，本地文件系统走 <see cref="IBundleStreamDecryptor"/> 流式加载路径。</remarks>
        public override IBundleDecryptor CreateDecryptor() => new FileStreamDecryptor();

        /// <inheritdoc />
        /// <remarks>内存实现单独提供：供 WebGL 系文件系统及本地流式解密失败后的兜底加载使用。</remarks>
        public override IBundleMemoryDecryptor CreateMemoryDecryptor() => new FileStreamMemoryDecryptor();

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
        /// 资源文件流加载解密类。
        /// </summary>
        /// <remarks>
        /// 仅实现 <see cref="IBundleStreamDecryptor"/>：YooAsset 本地加载按 Offset → Memory → Stream 分发，
        /// 若同时实现内存接口会导致流式解密被内存路径覆盖（整个资源包读入内存）。
        /// </remarks>
        class FileStreamDecryptor : IBundleStreamDecryptor
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
        }

        /// <summary>
        /// 资源文件内存解密类
        /// </summary>
        /// <remarks>
        /// 供 WebGL 系文件系统（仅支持内存解密）以及本地流式解密失败后的兜底加载使用。
        /// </remarks>
        class FileStreamMemoryDecryptor : IBundleMemoryDecryptor
        {
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
            /// <summary>
            /// XOR 密钥。
            /// </summary>
            /// <remarks>
            /// 安全边界说明：单字节 XOR 仅用于防止资源被普通用户直接打开/提取，
            /// 无法抵御逆向工程（密钥随客户端分发，可被提取）。
            /// 对资源安全有更高要求时，请自行实现更复杂的加密方案并派生 <see cref="ResourceEncryptorHandler"/>。
            /// </remarks>
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