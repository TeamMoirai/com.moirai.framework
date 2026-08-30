using System;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源加解密处理器基类。
    /// </summary>
    /// <remarks>
    /// 采用 <see cref="SerializableAttribute"/> + <see cref="UnityEngine.SerializeReference"/>
    /// 模式，使 Inspector 可通过 <c>ProviderDropdown</c> 下拉选择具体实现，
    /// 运行时通过 <see cref="CreateDecryptor"/> 创建解密器，构建时通过 <see cref="CreateEncryptor"/> 创建加密器。
    /// </remarks>
    [Serializable]
    public abstract class YooAssetEncryptorHandler
    {
        /// <summary>
        /// 创建运行时资源包解密器。
        /// </summary>
        /// <returns>解密器实例，无加密时返回 <c>null</c>。</returns>
        /// <remarks>
        /// 本地文件系统（内置/沙盒）使用；建议实现 <see cref="IBundleOffsetDecryptor"/> 或
        /// <see cref="IBundleStreamDecryptor"/> 以获得流式/偏移解密（内存占用更低）。
        /// 注意：若同时实现 <see cref="IBundleMemoryDecryptor"/>，YooAsset 的本地加载分发会优先走内存路径，
        /// 导致流式解密失效，因此内存实现应通过 <see cref="CreateMemoryDecryptor"/> 单独提供。
        /// </remarks>
        public abstract IBundleDecryptor CreateDecryptor();

        /// <summary>
        /// 创建内存解密器。
        /// </summary>
        /// <returns>内存解密器实例，无加密或不支持时返回 <c>null</c>。</returns>
        /// <remarks>
        /// WebGL 系文件系统（WebServer/WebNetwork/微信小游戏）仅支持内存解密，
        /// 同时也作为本地文件系统流式/偏移解密失败后的兜底加载方案。
        /// </remarks>
        public virtual IBundleMemoryDecryptor CreateMemoryDecryptor()
        {
            return CreateDecryptor() as IBundleMemoryDecryptor;
        }

        /// <summary>
        /// 创建构建时资源包加密器。
        /// </summary>
        /// <returns>加密器实例，无加密时返回 <c>null</c>。</returns>
        public abstract IBundleEncryptor CreateEncryptor();
    }
}
