using System;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// ResourceKeyCodec 的行为契约——位域编解码与 assetKind / assetType 归一。
    /// <para>这份代码是从 handler 的 Keys 里逐字搬出来的静态件，搬动本身不该改变任何语义，
    /// 所以这里钉的全是"改错一位就会静默造出重复键"的那几条：往返、越界必抛、
    /// 以及三条同名资源靠 kind/handleKind 分开。</para>
    /// </summary>
    public sealed class ResourceKeyCodecTests
    {
        [Test]
        public void Pack_Unpack_RoundTripsEveryAxisAtItsCeiling()
        {
            ulong key = ResourceKeyCodec.Pack(ResourceKeyCodec.RESOURCE_KEY_PACKAGE_MAX,
                ResourceKeyCodec.RESOURCE_KEY_LOCATION_MAX, ResourceKeyCodec.RESOURCE_KEY_TYPE_MAX,
                EResourceAssetKind.SubAssets, EResourceHandleKind.SubAssetsHandle);

            Assert.AreEqual(ResourceKeyCodec.RESOURCE_KEY_PACKAGE_MAX, ResourceKeyCodec.UnpackPackageId(key));
            Assert.AreEqual(ResourceKeyCodec.RESOURCE_KEY_LOCATION_MAX, ResourceKeyCodec.UnpackLocationId(key),
                "location 轴吃满 int.MaxValue：它是三条轴里唯一没有位宽余量可牺牲的，截断即两条图集共用一键");
            Assert.AreEqual(ResourceKeyCodec.RESOURCE_KEY_TYPE_MAX, ResourceKeyCodec.UnpackTypeId(key));
        }

        [Test]
        public void Pack_RejectsOutOfRange_InsteadOfTruncating()
        {
            Assert.Throws<GameException>(() => ResourceKeyCodec.Pack(0, 1, 1,
                EResourceAssetKind.Asset, EResourceHandleKind.AssetHandle), "id 0 是保留位，不得编进键");
            Assert.Throws<GameException>(() => ResourceKeyCodec.Pack(ResourceKeyCodec.RESOURCE_KEY_PACKAGE_MAX + 1, 1, 1,
                EResourceAssetKind.Asset, EResourceHandleKind.AssetHandle));
            Assert.Throws<GameException>(() => ResourceKeyCodec.Pack(1, 1, ResourceKeyCodec.RESOURCE_KEY_TYPE_MAX + 1,
                EResourceAssetKind.Asset, EResourceHandleKind.AssetHandle));
            // 负 id 走的是同一条 <=0 判定；位域若直接左移会把负数折成正数，编出一条合法却错主的键。
            Assert.Throws<GameException>(() => ResourceKeyCodec.Pack(1, -1, 1,
                EResourceAssetKind.Asset, EResourceHandleKind.AssetHandle));
        }

        [Test]
        public void Pack_KeepsAssetsApart_WhenOnlyKindOrHandleDiffers()
        {
            ulong asset = ResourceKeyCodec.Pack(1, 2, 3, EResourceAssetKind.Asset, EResourceHandleKind.AssetHandle);
            Assert.AreNotEqual(asset, ResourceKeyCodec.Pack(1, 2, 3, EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle), "同一 location 的资产与精灵是两条记录");
            Assert.AreNotEqual(asset, ResourceKeyCodec.Pack(1, 2, 3, EResourceAssetKind.Asset,
                EResourceHandleKind.SubAssetsHandle), "同一条资产的句柄种类不同也是两条记录");
            Assert.AreEqual(asset, ResourceKeyCodec.Pack(1, 2, 3, EResourceAssetKind.Asset,
                EResourceHandleKind.AssetHandle));
        }

        [Test]
        public void InferAssetKind_MatchesNormalizeAssetType()
        {
            Assert.AreEqual(EResourceAssetKind.Sprite, ResourceKeyCodec.InferAssetKind(typeof(Sprite)));
            Assert.AreEqual(EResourceAssetKind.Material, ResourceKeyCodec.InferAssetKind(typeof(Material)));
            Assert.AreEqual(EResourceAssetKind.Prefab, ResourceKeyCodec.InferAssetKind(typeof(GameObject)));
            Assert.AreEqual(EResourceAssetKind.Asset, ResourceKeyCodec.InferAssetKind(typeof(UObject)),
                "认不出的类型落到 Asset，而不是 Unknown——Unknown 只作为调用方「未指定」出现");

            // 两族互推必须稳定：先按类型推 kind，再按 kind 归一回类型，再推一次不得改变。
            foreach (Type type in new[] { typeof(Sprite), typeof(Material), typeof(GameObject), typeof(TextAsset) })
            {
                EResourceAssetKind kind = ResourceKeyCodec.InferAssetKind(type);
                Type normalized = ResourceKeyCodec.NormalizeAssetType(type, kind);
                Assert.AreEqual(kind, ResourceKeyCodec.InferAssetKind(normalized),
                    $"{type.Name} 归一之后 kind 变了，同一个键会被登记成两条轴");
            }
        }

        [Test]
        public void NormalizeAssetKind_OnlyFillsInWhenUnknown()
        {
            Assert.AreEqual(EResourceAssetKind.Prefab,
                ResourceKeyCodec.NormalizeAssetKind(typeof(Sprite), EResourceAssetKind.Prefab),
                "调用方显式给了 kind 就必须照它，不得被参数类型反过来改写");
            Assert.AreEqual(EResourceAssetKind.Sprite,
                ResourceKeyCodec.NormalizeAssetKind(typeof(Sprite), EResourceAssetKind.Unknown));
            // SubAssets 刻意映射到 typeof(Sprite)：图集键与精灵键共用同一条 type 轴才不会多出一个 id。
            Assert.AreEqual(typeof(Sprite),
                ResourceKeyCodec.NormalizeAssetType(typeof(UObject), EResourceAssetKind.SubAssets));
        }

        [Test]
        public void Unpack_OnZeroKey_YieldsNoAxis()
        {
            Assert.AreEqual(0, ResourceKeyCodec.UnpackPackageId(0UL));
            Assert.AreEqual(0, ResourceKeyCodec.UnpackLocationId(0UL));
            Assert.AreEqual(0, ResourceKeyCodec.UnpackTypeId(0UL));
        }
    }
}
