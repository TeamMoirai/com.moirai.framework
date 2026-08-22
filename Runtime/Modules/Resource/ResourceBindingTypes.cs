using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源绑定结果状态。
    /// </summary>
    public enum EResourceBindStatus : byte
    {
        /// <summary>
        /// 成功。
        /// </summary>
        Success = 0,

        /// <summary>
        /// 无效键。
        /// </summary>
        InvalidKey = 1,

        /// <summary>
        /// 缺少所有者。
        /// </summary>
        MissingOwner = 2,

        /// <summary>
        /// 缺少目标。
        /// </summary>
        MissingTarget = 3,

        /// <summary>
        /// 所有者已过期。
        /// </summary>
        StaleOwner = 4,

        /// <summary>
        /// 加载失败。
        /// </summary>
        LoadFailed = 6,

        /// <summary>
        /// 应用失败。
        /// </summary>
        ApplyFailed = 7,

        /// <summary>
        /// 服务已关闭。
        /// </summary>
        ServiceShutdown = 8,

        /// <summary>
        /// 未实现。
        /// </summary>
        NotImplemented = 9,
    }

    /// <summary>
    /// 资源绑定选项。
    /// </summary>
    [Flags]
    public enum EResourceBindingOption : byte
    {
        /// <summary>
        /// 无。
        /// </summary>
        None = 0,

        /// <summary>
        /// 释放时保持存活。
        /// </summary>
        KeepAliveOnRelease = 1,

        /// <summary>
        /// 设置原始尺寸。
        /// </summary>
        SetNativeSize = 2,
    }

    /// <summary>
    /// 资源绑定槽位类型。
    /// </summary>
    public enum EResourceBindingSlotType : byte
    {
        /// <summary>
        /// 无。
        /// </summary>
        None = 0,

        /// <summary>
        /// Image 精灵。
        /// </summary>
        ImageSprite = 1,

        /// <summary>
        /// Image 材质。
        /// </summary>
        ImageMaterial = 2,

        /// <summary>
        /// SpriteRenderer 精灵。
        /// </summary>
        SpriteRendererSprite = 3,

        /// <summary>
        /// Renderer 共享材质。
        /// </summary>
        RendererSharedMaterial = 4,

        /// <summary>
        /// Renderer 材质实例。
        /// </summary>
        RendererMaterialInstance = 5,

        /// <summary>
        /// 预制体源。
        /// </summary>
        PrefabSource = 6,

        /// <summary>
        /// 子精灵。
        /// </summary>
        SubSprite = 7,
    }

    /// <summary>
    /// 资源绑定服务接口。
    /// </summary>
    public interface IResourceBindingService
    {
        /// <summary>
        /// 注册所有者。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus RegisterOwner(ResourceOwner owner);

        /// <summary>
        /// 释放所有者。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus ReleaseOwner(ResourceOwner owner);

        /// <summary>
        /// 释放所有者。
        /// </summary>
        /// <param name="ownerId">所有者 ID。</param>
        /// <param name="generation">代际标记。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus ReleaseOwner(int ownerId, uint generation);

        /// <summary>
        /// 预热绑定记录容量。
        /// </summary>
        /// <param name="ownerCapacity">所有者容量。</param>
        /// <param name="bindingCapacity">绑定容量。</param>
        /// <param name="registeredTargetCapacity">已注册目标容量。</param>
        void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity);

        /// <summary>
        /// 注册目标组件。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="target">目标组件。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus RegisterTarget(ResourceOwner owner, Component target);

        /// <summary>
        /// 注销目标组件。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="target">目标组件。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus UnregisterTarget(ResourceOwner owner, Component target);

        /// <summary>
        /// 绑定精灵到 Image。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="image">目标 Image。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None);

        /// <summary>
        /// 绑定精灵到 SpriteRenderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus BindSprite(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None);

        /// <summary>
        /// 异步绑定子精灵到 Image。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="image">目标 Image。</param>
        /// <param name="atlasKey">图集资源标识键。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定结果状态。</returns>
        UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image, ResourceKey atlasKey,
            string spriteName, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步绑定子精灵到 SpriteRenderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="spriteRenderer">目标 SpriteRenderer。</param>
        /// <param name="atlasKey">图集资源标识键。</param>
        /// <param name="spriteName">精灵名称。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定结果状态。</returns>
        UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, SpriteRenderer spriteRenderer,
            ResourceKey atlasKey, string spriteName, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 绑定材质到 Image。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="image">目标 Image。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus BindImageMaterial(ResourceOwner owner, Image image, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None);

        /// <summary>
        /// 异步绑定材质到 Image。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="image">目标 Image。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定结果状态。</returns>
        UniTask<EResourceBindStatus> BindImageMaterialAsync(ResourceOwner owner, Image image, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None, CancellationToken cancellationToken = default);

        /// <summary>
        /// 绑定共享材质到 Renderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="renderer">目标 Renderer。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None);

        /// <summary>
        /// 异步绑定共享材质到 Renderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="renderer">目标 Renderer。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定结果状态。</returns>
        UniTask<EResourceBindStatus> BindSharedMaterialAsync(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None, CancellationToken cancellationToken = default);

        /// <summary>
        /// 绑定材质实例到 Renderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="renderer">目标 Renderer。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <returns>绑定结果状态。</returns>
        EResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None);

        /// <summary>
        /// 异步绑定材质实例到 Renderer。
        /// </summary>
        /// <param name="owner">资源所有者。</param>
        /// <param name="renderer">目标 Renderer。</param>
        /// <param name="key">资源标识键。</param>
        /// <param name="options">绑定选项。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>绑定结果状态。</returns>
        UniTask<EResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量获取所有者信息。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <param name="startIndex">起始索引。</param>
        /// <param name="maxCount">最大数量。</param>
        /// <returns>实际写入数量。</returns>
        int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount);

        /// <summary>
        /// 批量获取绑定信息。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <param name="startIndex">起始索引。</param>
        /// <param name="maxCount">最大数量。</param>
        /// <returns>实际写入数量。</returns>
        int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount);
    }
}
