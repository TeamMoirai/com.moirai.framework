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
    public abstract class ResourceEncryptorHandler
    {
        /// <summary>
        /// 创建运行时资源包解密器。
        /// </summary>
        /// <returns>解密器实例，无加密时返回 <c>null</c>。</returns>
        public abstract IBundleDecryptor CreateDecryptor();

        /// <summary>
        /// 创建构建时资源包加密器。
        /// </summary>
        /// <returns>加密器实例，无加密时返回 <c>null</c>。</returns>
        public abstract IBundleEncryptor CreateEncryptor();
    }
}
