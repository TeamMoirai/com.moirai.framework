#if ADDRESSABLES_INSTALLED
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 基于 Unity Addressables 的资源处理器实现（实验性），与 <see cref="YooAssetHandler"/> 共用 <see cref="ResourceRecordStore"/> 记录内核。
    /// </summary>
    /// <remarks>
    /// 异步租约 / 绑定 / 预制体实例化 / 图集子精灵 / 场景加载 / 缓存维护与低内存回收均为对等实现。 <br />
    /// Addressables 没有同步加载 API 与两步式 Check→Update 下载器，故同步取用族与下载族统一抛 <see cref="GameException"/> fail-fast。 <br />
    /// 只有异步可答的查询（<c>IsNeedDownloadFromRemote</c> / <c>GetPackageVersion</c> / <c>GetAssetInfo</c> / 按标签的 <br />
    /// <c>GetAssetInfos</c>）退化为恒定值。
    /// </remarks>
    [ProviderDisplay(title: "Addressables", description: "Unity Addressables 后端（实验性）：无同步加载族，下载族 fail-fast")]
    [Serializable]
    internal sealed partial class AddressableHandler : ResourceServiceHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <inheritdoc />
        public override string DefaultPackageName { get; set; } = "Default";

        /// <inheritdoc />
        public override IResourceBindingService BindingService => _bindingService;

        private ResourceBindingService _bindingService;

        /// <inheritdoc />
        public override string HostServerURL { get; set; }

        /// <inheritdoc />
        public override string FallbackHostServerURL { get; set; }

        /// <inheritdoc />
        public override EResourceLoadWayWebGL LoadResWayWebGL { get; set; }

        /// <inheritdoc />
        public override string ApplicableGameVersion => Application.version;

        /// <inheritdoc />
        public override int InternalResourceVersion => 0;

        /// <inheritdoc />
        public override string PackageVersion { get; set; }

        /// <inheritdoc />
        public override bool UpdatableWhilePlaying => false;

        /// <inheritdoc />
        public override bool AutoUnloadBundleWhenUnused { get; set; }

        /// <inheritdoc />
        public override int DownloadingMaxNum { get; set; }

        /// <inheritdoc />
        public override int FailedTryAgain { get; set; }

        /// <inheritdoc />
        public override long Milliseconds { get; set; }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 构建实验性后端能力缺失异常：由调用方以 throw 语句抛出，保证非 void 成员的代码路径终止性。
        /// </summary>
        /// <param name="api">触发失败的调用方成员名。</param>
        /// <returns>预构建的 GameException。</returns>
        private static GameException CreateNotSupported([CallerMemberName] string api = null)
        {
            return new GameException(StringUtility.Format(
                "[AddressableHandler] {0} is not implemented. This experimental backend covers the async lease/binding " +
                "families only; use the async counterpart or YooAssetHandler.",
                api ?? "API"));
        }

        #endregion
    }
}
#endif
