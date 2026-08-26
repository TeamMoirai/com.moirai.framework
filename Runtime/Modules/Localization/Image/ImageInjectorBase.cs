using Cysharp.Threading.Tasks;
using UnityEngine;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 基于图片的本地化注入器基类，共享以下通用模式：<br/>
    /// - 检查本地化使用的是索引还是资源文本 ID<br/>
    /// - 从资源系统异步加载资源<br/>
    /// - 处理 Sprite/Texture 类型转换，并输出相应日志
    /// </summary>
    public abstract class ImageInjectorBase : IInjector
    {
        private readonly string _localizedTextID;

        protected ImageInjectorBase(string localizedTextID)
        {
            _localizedTextID = localizedTextID;
        }

        public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
        {
            if (localizedData is int index)
            {
                if (string.IsNullOrEmpty(_localizedTextID))
                {
                    ApplyFromArray(index);
                }
                else
                {
                    ApplyFromResource().Forget();
                }
            }
        }

        /// <summary>
        /// 通过索引从预分配的数组中应用本地化资源。
        /// </summary>
        protected abstract void ApplyFromArray(int index);

        /// <summary>
        /// 将加载到的资源应用到目标组件。<br/>
        /// 在资源成功加载并通过验证后调用。
        /// </summary>
        protected abstract void ApplyAsset(Object asset);

        /// <summary>
        /// 获取预期资源类型名称，用于错误消息。
        /// </summary>
        protected abstract string GetExpectedTypeName();

        /// <summary>
        /// 尝试转换不匹配的资源类型并应用。<br/>
        /// 如果转换已处理则返回 true，否则返回 false。
        /// </summary>
        protected abstract bool TryConvertAndApply(Object asset);

        private async UniTaskVoid ApplyFromResource()
        {
            string textIDValue = LocalizationService.GetTextFromId(_localizedTextID);
            var result = await ResourceService.LoadAssetAsync<Object>(textIDValue);

            if (!IsExpectedType(result) && !IsConvertibleType(result))
            {
                LogUtility.Error("Localized image type error, {0}", textIDValue);
            }

            if (TryConvertAndApply(result))
            {
                LogUtility.Warning("Localized image type error, automatically converted: {0}", result.ToString());
                return;
            }

            ApplyAsset(result);
        }

        /// <summary>
        /// 检查加载的资源是否为预期的主要类型。
        /// </summary>
        protected abstract bool IsExpectedType(Object asset);

        /// <summary>
        /// 检查加载的资源是否为可转换的类型。
        /// </summary>
        protected abstract bool IsConvertibleType(Object asset);
    }
}
