using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 基于图片的本地化注入器基类，共享以下通用模式：<br />
    /// - 按载荷类型派发：<see cref="int"/> 为语言下标（数组模式），<see cref="string"/> 为资源 location（资源模式）<br />
    /// - 从资源系统异步加载资源（租约由注入器持有，切换语言时释放上一份，销毁时随 IDisposable 释放）<br />
    /// - 处理 Sprite/Texture 类型转换，并输出相应日志
    /// </summary>
    public abstract class ImageInjectorBase : ILocalizationInjector, IDisposable
#if UNITY_EDITOR
        , IInjectorAssetPreview
#endif
    {
        // 当前语言图片资源的租约：持有引用防止资源在显示期间被周期性 UnloadUnusedAssets 回收
        private ResourceAssetLease<UObject> _currentLease;
        // 加载版本号：语言快速连续切换或销毁时丢弃过期的异步加载结果
        private int _loadVersion;

        /// <summary>
        /// 按载荷类型注入本地化数据。
        /// <para><see cref="int"/> = 当前语言下标，从预分配数组取用；<see cref="string"/> = 资源 location
        /// （本地化器已经 <c>TryGetTextFromId</c> 单趟解析），异步加载后应用。其余载荷类型忽略。</para>
        /// </summary>
        /// <typeparam name="T1">载荷类型：<see cref="int"/> 或 <see cref="string"/>。</typeparam>
        /// <typeparam name="T2">本地化器类型。</typeparam>
        /// <param name="localizedData">语言下标或资源 location。</param>
        /// <param name="localizer">发起注入的本地化器。</param>
        public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
        {
            switch (localizedData)
            {
                case int index:
                    ApplyFromArray(index);
                    break;
                case string location:
                    ApplyFromResource(location).Forget();
                    break;
            }
        }

        /// <summary>
        /// 释放当前持有的资源租约，并使在途异步加载失效。
        /// </summary>
        public void Dispose()
        {
            // 递增版本号：销毁后完成的加载会自行丢弃并释放租约，避免泄漏
            _loadVersion++;
            _currentLease.Dispose();
            _currentLease = default;
            OnDispose();
        }

        /// <summary>
        /// 清除目标组件上的本地化内容，并释放资源租约。
        /// </summary>
        public void Clear()
        {
            Dispose();
            ClearTarget();
        }

        /// <summary>
        /// 释放子类额外持有的运行时对象（如 Texture2D 转换出的 Sprite）。
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        /// <summary>
        /// 将目标组件上的本地化显示内容置空。
        /// </summary>
        protected abstract void ClearTarget();

        /// <summary>
        /// 通过索引从预分配的数组中应用本地化资源。
        /// </summary>
        protected abstract void ApplyFromArray(int index);

        /// <summary>
        /// 将加载到的资源应用到目标组件。<br />
        /// 在资源成功加载并通过验证后调用。
        /// </summary>
        protected abstract void ApplyAsset(UObject asset);

        /// <summary>
        /// 获取预期资源类型名称，用于错误消息。
        /// </summary>
        protected abstract string GetExpectedTypeName();

        /// <summary>
        /// 尝试转换不匹配的资源类型并应用。<br />
        /// 如果转换已处理则返回 true，否则返回 false。
        /// </summary>
        protected abstract bool TryConvertAndApply(UObject asset);

        /// <summary>
        /// 按资源 location 从资源系统异步加载并应用。
        /// </summary>
        private async UniTaskVoid ApplyFromResource(string location)
        {
            var version = ++_loadVersion;
            var lease = await ResourceService.LoadLeaseAsync<UObject>(location);

            // 加载期间发生了更新的切换或已销毁，丢弃过期结果
            if (version != _loadVersion)
            {
                lease.Dispose();
                return;
            }

            if (!lease.IsValid)
            {
                LogUtility.Error("Localized image load failed: {0}", location);
                return;
            }

            if (!IsExpectedType(lease.Asset) && !IsConvertibleType(lease.Asset))
            {
                LogUtility.Error("Localized image type error, expected {0}: {1}", GetExpectedTypeName(), location);
                lease.Dispose();
                return;
            }

            // 释放上一份语言的租约（Dispose 内部对未持有状态短路），持有本次资源直到下次加载或销毁
            _currentLease.Dispose();
            _currentLease = lease;

            if (TryConvertAndApply(lease.Asset))
            {
                LogUtility.Warning("Localized image type error, automatically converted: {0}", lease.Asset.name);
                return;
            }

            ApplyAsset(lease.Asset);
        }

        /// <summary>
        /// 检查加载的资源是否为预期的主要类型。
        /// </summary>
        protected abstract bool IsExpectedType(UObject asset);

        /// <summary>
        /// 检查加载的资源是否为可转换的类型。
        /// </summary>
        protected abstract bool IsConvertibleType(UObject asset);

#if UNITY_EDITOR
        // 预览判据全部转发到加载期那三个判据：一处口径，预览与注入不会分叉。
        string IInjectorAssetPreview.ExpectedTypeName => GetExpectedTypeName();

        bool IInjectorAssetPreview.Accepts(UObject asset) => IsExpectedType(asset);

        bool IInjectorAssetPreview.Converts(UObject asset) => IsConvertibleType(asset);
#endif
    }
}
