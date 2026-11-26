using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// TMP 图文混排引用：<see cref="TMP_SpriteAsset"/> 图集 + 图集内下标，
    /// 供配置资产（货币、伤害类型等）与 UI 组件以 <c>&lt;sprite=N tint&gt;</c> 富文本嵌图。
    /// </summary>
    /// <remarks>
    /// 检视绘制在 Editor 侧 <c>SpriteAssetIndexDrawer</c>（Moirai.Atropos.Asset）：
    /// 图集弱引用一行（AssetReference 默认 Drawer 实现）、下标用按图集精灵数钳制的滑动条、
    /// 命中精灵预览紧贴字段左侧（对齐旧版 CurrencyPane 口径）；
    /// 取像走 <see cref="AssetService"/>（保活窗口口径），富文本由 <see cref="BuildSpriteTag"/> 生成。
    /// </remarks>
    [Serializable]
    public class SpriteAssetIndex
    {

        #region 字段 [FIELDS]

        [Tooltip("图文混排的 TMP 精灵图集（弱引用）")]
        [SerializeField] private AssetReference<TMP_SpriteAsset> m_SpriteAsset = new AssetReference<TMP_SpriteAsset>();

        [Tooltip("精灵在图集中的下标")]
        [SerializeField] private int m_SpriteIndex;

        private TMP_SpriteAsset _spriteAssetCache;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>图文混排的精灵图集弱引用。</summary>
        public AssetReference<TMP_SpriteAsset> SpriteAsset => m_SpriteAsset;

        /// <summary>精灵在图集中的下标。</summary>
        public int SpriteIndex => m_SpriteIndex;

        /// <summary>图集引用是否已配置。</summary>
        public bool IsValid => m_SpriteAsset != null && !string.IsNullOrEmpty(m_SpriteAsset.GUID);

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 异步取图集（GUID 寻址走 <see cref="AssetService"/>，编辑器预览回退 AssetDatabase）；取空返回 null。
        /// </summary>
        public async UniTask<TMP_SpriteAsset> GetSpriteAssetAsync(CancellationToken cancellationToken = default)
        {
            if (_spriteAssetCache != null) return _spriteAssetCache;
            if (!IsValid) return null;

            if (m_SpriteAsset.TryGetLocation(out string location))
            {
                _spriteAssetCache = await AssetService.TryLoadAssetAsync<TMP_SpriteAsset>(location, cancellationToken, m_SpriteAsset.PackageName);
            }

            return _spriteAssetCache;
        }

        /// <summary>
        /// 生成图文混排富文本标签（<c>&lt;sprite=N tint&gt;</c>）；
        /// 由调用方把 <see cref="GetSpriteAssetAsync"/> 的结果挂到目标 <see cref="UILabel"/> 后才会实际显示。
        /// </summary>
        public string BuildSpriteTag()
        {
            return $"<sprite={m_SpriteIndex} tint>";
        }

        #endregion

    }
}
