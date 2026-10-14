using System;
using System.IO;
using System.Text;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// SpriteReference 引用门面的纯数据契约：存档块往返与线格式、版本守卫、等值比较与克隆独立性。
    /// </summary>
    /// <remarks>
    /// 存档块的线格式与玩法侧 IOExtensions 的 <c>WriteInt</c>/<c>WriteStringPacket</c> 字节兼容， <br />
    /// 由 <c>WireFormatByteLayout_StaysCompatible</c> 以手写字节序列钉死，防止编解码漂移。
    /// </remarks>
    public sealed class SpriteReferenceTests
    {
        private const string TEST_GUID = "0123456789abcdef0123456789abcdef";
        private const string TEST_PACKAGE = "TestPackage";

        #region 存档块 [SAVE BLOCK]

        [Test]
        public void SaveLoad_RoundTrip_PreservesAllFields()
        {
            SpriteReference source = BuildFullReference();
            SpriteReference restored = new SpriteReference();

            using (MemoryStream stream = new MemoryStream())
            {
                source.DataSave(stream);
                stream.Position = 0;
                restored.DataLoad(stream);
            }

            Assert.AreEqual(source.PackageType, restored.PackageType);
            Assert.AreEqual(source.SpriteRef.GUID, restored.SpriteRef.GUID);
            Assert.AreEqual(source.SpriteRef.PackageName, restored.SpriteRef.PackageName);
            Assert.AreEqual(source.SpriteName, restored.SpriteName);
            Assert.AreEqual(source.ConfigId, restored.ConfigId);
        }

        [Test]
        public void SaveLoad_CommonMode_RoundTrip_PreservesSubSpriteName()
        {
            SpriteReference source = new SpriteReference
            {
                SpriteRef = new AssetReference<Sprite> { GUID = TEST_GUID, PackageName = string.Empty },
                SpriteName = "sub-icon",
            };
            SpriteReference restored = new SpriteReference();

            using (MemoryStream stream = new MemoryStream())
            {
                source.DataSave(stream);
                stream.Position = 0;
                restored.DataLoad(stream);
            }

            Assert.AreEqual(SpriteReference.ESpritePackageType.Common, restored.PackageType);
            Assert.AreEqual(TEST_GUID, restored.SpriteRef.GUID);
            Assert.AreEqual("sub-icon", restored.SpriteName);
            Assert.AreEqual(string.Empty, restored.ConfigId);
        }

        [Test]
        public void WireFormatByteLayout_StaysCompatible()
        {
            // 线格式：int 版本 + int 打包方式 + 4 组「long 长度前缀 + UTF-8」字符串（GUID/包名/子图名/配置表 ID），
            // 与玩法侧 IOExtensions 的 WriteInt / WriteStringPacket 逐字节对齐
            byte[] block = BuildWireBlock(SpriteReference.CURRENT_VERSION,
                SpriteReference.ESpritePackageType.Config, TEST_GUID, TEST_PACKAGE, "sub-icon", "cfg-1001");
            SpriteReference restored = new SpriteReference();

            using (MemoryStream stream = new MemoryStream(block))
            {
                restored.DataLoad(stream);
            }

            Assert.AreEqual(SpriteReference.ESpritePackageType.Config, restored.PackageType);
            Assert.AreEqual(TEST_GUID, restored.SpriteRef.GUID);
            Assert.AreEqual(TEST_PACKAGE, restored.SpriteRef.PackageName);
            Assert.AreEqual("sub-icon", restored.SpriteName);
            Assert.AreEqual("cfg-1001", restored.ConfigId);
        }

        [Test]
        public void DataLoad_FutureVersion_ThrowsInvalidDataException()
        {
            // 版本号写为超界值，加载必须在读任何字段前拒绝
            byte[] block = BuildWireBlock(SpriteReference.CURRENT_VERSION + 1,
                SpriteReference.ESpritePackageType.Common, string.Empty, string.Empty, string.Empty, string.Empty);

            Assert.Throws<InvalidDataException>(() =>
            {
                using MemoryStream stream = new MemoryStream(block);
                new SpriteReference().DataLoad(stream);
            });
        }

        #endregion

        #region 等值与克隆 [MATCHES & CLONE]

        [Test]
        public void Matches_EqualInstances_ReturnsTrue()
        {
            Assert.IsTrue(BuildFullReference().Matches(BuildFullReference()));
        }

        [Test]
        public void Matches_AnyFieldDiffers_ReturnsFalse()
        {
            SpriteReference baseline = BuildFullReference();

            SpriteReference packageTypeVariant = BuildFullReference();
            packageTypeVariant.PackageType = SpriteReference.ESpritePackageType.Common;
            SpriteReference guidVariant = BuildFullReference();
            guidVariant.SpriteRef = new AssetReference<Sprite> { GUID = "ffffffffffffffffffffffffffffffff", PackageName = TEST_PACKAGE };
            SpriteReference packageNameVariant = BuildFullReference();
            packageNameVariant.SpriteRef = new AssetReference<Sprite> { GUID = TEST_GUID, PackageName = "Other" };
            SpriteReference spriteNameVariant = BuildFullReference();
            spriteNameVariant.SpriteName = "other-icon";
            SpriteReference configIdVariant = BuildFullReference();
            configIdVariant.ConfigId = "cfg-9999";

            Assert.IsFalse(baseline.Matches(packageTypeVariant), "打包方式不同不得判等");
            Assert.IsFalse(baseline.Matches(guidVariant), "GUID 不同不得判等");
            Assert.IsFalse(baseline.Matches(packageNameVariant), "包名不同不得判等");
            Assert.IsFalse(baseline.Matches(spriteNameVariant), "子图名不同不得判等");
            Assert.IsFalse(baseline.Matches(configIdVariant), "配置表 ID 不同不得判等");
        }

        [Test]
        public void Matches_NullOther_ReturnsFalse()
        {
            Assert.IsFalse(BuildFullReference().Matches(null));
        }

        [Test]
        public void Matches_NullVsEmptyStrings_TreatedEqual()
        {
            SpriteReference withNulls = new SpriteReference();
            SpriteReference withEmpties = new SpriteReference
            {
                SpriteRef = new AssetReference<Sprite> { GUID = string.Empty, PackageName = string.Empty },
                SpriteName = string.Empty,
                ConfigId = string.Empty,
            };

            Assert.IsTrue(withNulls.Matches(withEmpties));
            Assert.IsTrue(withEmpties.Matches(withNulls));
        }

        [Test]
        public void Clone_CopiesFields_ButReferenceInstanceIsIndependent()
        {
            SpriteReference source = BuildFullReference();

            SpriteReference clone = source.Clone();

            Assert.AreEqual(source.PackageType, clone.PackageType);
            Assert.AreEqual(source.SpriteRef.GUID, clone.SpriteRef.GUID);
            Assert.AreEqual(source.SpriteRef.PackageName, clone.SpriteRef.PackageName);
            Assert.AreEqual(source.SpriteName, clone.SpriteName);
            Assert.AreEqual(source.ConfigId, clone.ConfigId);
            Assert.AreNotSame(source.SpriteRef, clone.SpriteRef, "克隆必须换新弱引用实例，加载状态不共享");
        }

        #endregion

        #region 子图名判别 [SUB-SPRITE NAME]

        [Test]
        public void ResolveSubSpriteName_NullInputs_ReturnEmpty()
        {
            Assert.AreEqual(string.Empty, SpriteReference.ResolveSubSpriteName(null, "Assets/x.png"));
            Assert.AreEqual(string.Empty, SpriteReference.ResolveSubSpriteName(null, string.Empty));
        }

        #endregion

        #region 辅助 [HELPERS]

        /// <summary>
        /// 全字段填充的基准实例（Config 模式）。
        /// </summary>
        private static SpriteReference BuildFullReference()
        {
            return new SpriteReference
            {
                PackageType = SpriteReference.ESpritePackageType.Config,
                SpriteRef = new AssetReference<Sprite> { GUID = TEST_GUID, PackageName = TEST_PACKAGE },
                SpriteName = "sub-icon",
                ConfigId = "cfg-1001",
            };
        }

        /// <summary>
        /// 按线格式手写存档块（不经被测代码），钉死字节布局。
        /// </summary>
        private static byte[] BuildWireBlock(int version, SpriteReference.ESpritePackageType packageType,
            string guid, string packageName, string spriteName, string configId)
        {
            using MemoryStream stream = new MemoryStream();

            WriteInt(stream, version);
            WriteInt(stream, (int)packageType);
            WriteStringPacket(stream, guid);
            WriteStringPacket(stream, packageName);
            WriteStringPacket(stream, spriteName);
            WriteStringPacket(stream, configId);

            return stream.ToArray();
        }

        private static void WriteInt(Stream stream, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        private static void WriteStringPacket(Stream stream, string value)
        {
            byte[] b = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] length = BitConverter.GetBytes((long)b.Length);
            stream.Write(length, 0, length.Length);
            stream.Write(b, 0, b.Length);
        }

        #endregion

    }
}
