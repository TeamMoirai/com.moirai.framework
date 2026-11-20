using System;
using Moirai.Atropos;
using Moirai.Atropos.Asset;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Asset
{
    /// <summary>
    /// <see cref="AssetKeyCodec"/> 的行为契约：位域编解码与 <c>assetKind</c>/<c>assetType</c> 归一。
    /// </summary>
    /// <remarks>
    /// 钉的是"改错一位就会静默造出重复键"的那几条：往返、越界必抛、
    /// 三条同名资源靠 <c>kind</c>/<c>handleKind</c> 分开。
    /// </remarks>
    public sealed class AssetKeyCodecTests
    {
        [Test]
        public void Pack_Unpack_RoundTripsEveryAxisAtItsCeiling()
        {
            ulong key = AssetKeyCodec.Pack(AssetKeyCodec.RESOURCE_KEY_PACKAGE_MAX,
                AssetKeyCodec.RESOURCE_KEY_LOCATION_MAX, AssetKeyCodec.RESOURCE_KEY_TYPE_MAX,
                EAssetKind.SubAssets, EAssetHandleKind.SubAssetsHandle);

            Assert.AreEqual(AssetKeyCodec.RESOURCE_KEY_PACKAGE_MAX, AssetKeyCodec.UnpackPackageId(key));
            Assert.AreEqual(AssetKeyCodec.RESOURCE_KEY_LOCATION_MAX, AssetKeyCodec.UnpackLocationId(key),
                "location 轴吃满 int.MaxValue：它是三条轴里唯一没有位宽余量可牺牲的，截断即两条图集共用一键");
            Assert.AreEqual(AssetKeyCodec.RESOURCE_KEY_TYPE_MAX, AssetKeyCodec.UnpackTypeId(key));
        }

        [Test]
        public void Pack_RejectsOutOfRange_InsteadOfTruncating()
        {
            Assert.Throws<MoiraiException>(() => AssetKeyCodec.Pack(0, 1, 1,
                EAssetKind.Asset, EAssetHandleKind.AssetHandle), "id 0 是保留位，不得编进键");
            Assert.Throws<MoiraiException>(() => AssetKeyCodec.Pack(AssetKeyCodec.RESOURCE_KEY_PACKAGE_MAX + 1, 1, 1,
                EAssetKind.Asset, EAssetHandleKind.AssetHandle));
            Assert.Throws<MoiraiException>(() => AssetKeyCodec.Pack(1, 1, AssetKeyCodec.RESOURCE_KEY_TYPE_MAX + 1,
                EAssetKind.Asset, EAssetHandleKind.AssetHandle));
            // 负 id 走的是同一条 <=0 判定；位域若直接左移会把负数折成正数，编出一条合法却错主的键。
            Assert.Throws<MoiraiException>(() => AssetKeyCodec.Pack(1, -1, 1,
                EAssetKind.Asset, EAssetHandleKind.AssetHandle));
        }

        [Test]
        public void Pack_KeepsAssetsApart_WhenOnlyKindOrHandleDiffers()
        {
            ulong asset = AssetKeyCodec.Pack(1, 2, 3, EAssetKind.Asset, EAssetHandleKind.AssetHandle);
            Assert.AreNotEqual(asset, AssetKeyCodec.Pack(1, 2, 3, EAssetKind.Sprite,
                EAssetHandleKind.AssetHandle), "同一 location 的资产与精灵是两条记录");
            Assert.AreNotEqual(asset, AssetKeyCodec.Pack(1, 2, 3, EAssetKind.Asset,
                EAssetHandleKind.SubAssetsHandle), "同一条资产的句柄种类不同也是两条记录");
            Assert.AreEqual(asset, AssetKeyCodec.Pack(1, 2, 3, EAssetKind.Asset,
                EAssetHandleKind.AssetHandle));
        }

        [Test]
        public void InferAssetKind_MatchesNormalizeAssetType()
        {
            Assert.AreEqual(EAssetKind.Sprite, AssetKeyCodec.InferAssetKind(typeof(Sprite)));
            Assert.AreEqual(EAssetKind.Material, AssetKeyCodec.InferAssetKind(typeof(Material)));
            Assert.AreEqual(EAssetKind.Prefab, AssetKeyCodec.InferAssetKind(typeof(GameObject)));
            Assert.AreEqual(EAssetKind.Asset, AssetKeyCodec.InferAssetKind(typeof(UObject)),
                "认不出的类型落到 Asset，而不是 Unknown——Unknown 只作为调用方「未指定」出现");

            // 两族互推必须稳定：先按类型推 kind，再按 kind 归一回类型，再推一次不得改变。
            foreach (Type type in new[] { typeof(Sprite), typeof(Material), typeof(GameObject), typeof(TextAsset) })
            {
                EAssetKind kind = AssetKeyCodec.InferAssetKind(type);
                Type normalized = AssetKeyCodec.NormalizeAssetType(type, kind);
                Assert.AreEqual(kind, AssetKeyCodec.InferAssetKind(normalized),
                    $"{type.Name} 归一之后 kind 变了，同一个键会被登记成两条轴");
            }
        }

        [Test]
        public void NormalizeAssetKind_OnlyFillsInWhenUnknown()
        {
            Assert.AreEqual(EAssetKind.Prefab,
                AssetKeyCodec.NormalizeAssetKind(typeof(Sprite), EAssetKind.Prefab),
                "调用方显式给了 kind 就必须照它，不得被参数类型反过来改写");
            Assert.AreEqual(EAssetKind.Sprite,
                AssetKeyCodec.NormalizeAssetKind(typeof(Sprite), EAssetKind.Unknown));
            // SubAssets 刻意映射到 typeof(Sprite)：图集键与精灵键共用同一条 type 轴才不会多出一个 id。
            Assert.AreEqual(typeof(Sprite),
                AssetKeyCodec.NormalizeAssetType(typeof(UObject), EAssetKind.SubAssets));
        }

        [Test]
        public void Unpack_OnZeroKey_YieldsNoAxis()
        {
            Assert.AreEqual(0, AssetKeyCodec.UnpackPackageId(0UL));
            Assert.AreEqual(0, AssetKeyCodec.UnpackLocationId(0UL));
            Assert.AreEqual(0, AssetKeyCodec.UnpackTypeId(0UL));
        }
    }
}
