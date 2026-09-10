using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;

namespace Service.Save
{
    /// <summary>
    /// 生成捕获器行为测试：注册表自注册、全字段捕获/恢复往返、掩码过滤、未知键跳过与缺失键保留当前值。
    /// <para>测试组件声明在本程序集（partial + internal，捕获器经 SaveHostGenerator 生成并模块初始化器注册）。</para>
    /// </summary>
    public class SaveCapturerTests
    {
        /// <summary>测试枚举。</summary>
        internal enum ETestMode
        {
            Idle,
            Run,
        }

        /// <summary>测试组件（字段覆盖：基元/枚举/字符串/Unity 数学类型；含私有字段验证生成捕获器的私有访问）。</summary>
        internal partial class KvTestComponent : MonoBehaviour
        {
            [SaveField]
            internal int Hp;

            [SaveField("player_name")]
            internal string PlayerName;

            [SaveField]
            private float Speed;

            [SaveField]
            internal Vector3 Position;

            [SaveField]
            internal Quaternion Rotation;

            [SaveField]
            internal ETestMode Mode;

            [SaveField]
            internal bool Active;

            [SaveField]
            internal long Coins;

            /// <summary>私有字段读取（测试断言用）。</summary>
            internal float SpeedValue => Speed;

            /// <summary>私有字段写入（测试准备用）。</summary>
            internal void SetSpeed(float value)
            {
                Speed = value;
            }
        }

        [SetUp]
        public void SetUp()
        {
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (gameObject.scene.IsValid() && gameObject.name.StartsWith("kv-test-", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }

        /// <summary>
        /// 创建挂载测试组件的 GameObject。
        /// </summary>
        private KvTestComponent CreateComponent(string name)
        {
            var gameObject = new GameObject("kv-test-" + name);
            return gameObject.AddComponent<KvTestComponent>();
        }

        [Test]
        public void Registry_AutoRegistersGeneratedCapturer()
        {
            // 模块初始化器在程序集加载时注册——注册表应已包含测试组件类型
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(KvTestComponent), out ISaveComponentCapturer capturer));
            Assert.AreEqual(typeof(KvTestComponent), capturer.ComponentType);
            Assert.AreEqual(8, capturer.FieldNames.Length, "应枚举全部 [SaveField] 字段（含私有）");
            CollectionAssert.Contains(capturer.FieldNames, "player_name", "显式键应生效");
            CollectionAssert.Contains(capturer.FieldNames, "Speed", "私有字段应被捕获");
        }

        [Test]
        public void Capture_ThenRestore_RoundTripsAllFields()
        {
            KvTestComponent source = CreateComponent("source");
            source.Hp = 90;
            source.PlayerName = "Moirai⑵";
            source.SetSpeed(3.5f);
            source.Position = new Vector3(1f, 2f, 3f);
            source.Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            source.Mode = ETestMode.Run;
            source.Active = true;
            source.Coins = 1234567890123L;

            KvTestComponent target = CreateComponent("target");
            target.SetSpeed(0f);

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(KvTestComponent), out ISaveComponentCapturer capturer));
            var allKeys = new HashSet<string>(capturer.FieldNames);
            var allMask = new SaveFieldMask(capturer.FieldNames, allKeys);

            var writer = new SaveKeyValueWriter(256);
            capturer.Capture(source, ref writer, allMask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            capturer.Restore(target, ref reader, capturer.FieldNames.Length, allMask);

            Assert.AreEqual(90, target.Hp);
            Assert.AreEqual("Moirai⑵", target.PlayerName);
            Assert.AreEqual(3.5f, target.SpeedValue, 0.0001f);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.Position);
            Assert.AreEqual(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f), target.Rotation);
            Assert.AreEqual(ETestMode.Run, target.Mode);
            Assert.IsTrue(target.Active);
            Assert.AreEqual(1234567890123L, target.Coins);
        }

        [Test]
        public void Capture_MaskFiltersDisabledFields()
        {
            KvTestComponent source = CreateComponent("masked-source");
            source.Hp = 77;
            source.Coins = 555;

            KvTestComponent target = CreateComponent("masked-target");
            target.Hp = 1;
            target.Coins = 2;

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(KvTestComponent), out ISaveComponentCapturer capturer));
            var partialMask = new SaveFieldMask(capturer.FieldNames, new HashSet<string> { "Hp" });

            var writer = new SaveKeyValueWriter(64);
            capturer.Capture(source, ref writer, partialMask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            capturer.Restore(target, ref reader, 1, partialMask);

            Assert.AreEqual(77, target.Hp, "掩码内字段应恢复");
            Assert.AreEqual(2, target.Coins, "掩码外字段应保留目标当前值");
        }

        [Test]
        public void Restore_SkipsUnknownKeys_KeepsCurrentValues()
        {
            // 向后兼容模拟：旧存档含已废弃字段（unknown_key）+ 现存字段（Hp）——废弃键跳过、现存键恢复
            var writer = new SaveKeyValueWriter(64);
            writer.WriteBoolean("unknown_key", true);
            writer.WriteInt32("Hp", 42);

            KvTestComponent target = CreateComponent("unknown-target");
            target.Hp = 1;
            target.Coins = 9;

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(KvTestComponent), out ISaveComponentCapturer capturer));
            var allKeys = new HashSet<string>(capturer.FieldNames);
            var allMask = new SaveFieldMask(capturer.FieldNames, allKeys);

            var reader = new SaveKeyValueReader(writer.ToArray());
            // 捕获期记录数 = 2（未知键也占记录），恢复按块内记录数消费
            capturer.Restore(target, ref reader, 2, allMask);

            Assert.AreEqual(42, target.Hp, "现存键应恢复");
            Assert.AreEqual(9, target.Coins, "未出现在存档中的字段应保留当前值");
        }

        [Test]
        public void Restore_NullString_RestoresNull()
        {
            KvTestComponent source = CreateComponent("null-string-source");
            source.PlayerName = null;

            KvTestComponent target = CreateComponent("null-string-target");
            target.PlayerName = "not-null";

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(KvTestComponent), out ISaveComponentCapturer capturer));
            var allKeys = new HashSet<string>(capturer.FieldNames);
            var allMask = new SaveFieldMask(capturer.FieldNames, allKeys);

            var writer = new SaveKeyValueWriter(64);
            capturer.Capture(source, ref writer, allMask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            capturer.Restore(target, ref reader, capturer.FieldNames.Length, allMask);

            Assert.IsNull(target.PlayerName, "Null 记录应恢复为 null 字符串");
        }
    }
}
