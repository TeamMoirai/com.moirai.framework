using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// SaveHost SG v2 捕获器测试：集合（List/Dictionary/Stack/数组）、嵌套 [SaveData] 数据类、
    /// 场景对象引用（SaveObjectIdentity 稳定 ID）、资产引用（SaveAssetCatalog 定位串）的捕获/恢复往返。
    /// <para>测试组件依赖 SaveHost SG 生成捕获器（2026-09-11 起测试程序集生成链路已修复）；
    /// <see cref="Capturer_IsRegistered"/> 失败即生成链路回归信号。</para>
    /// </summary>
    public partial class SaveCapturerV2Tests
    {
        /// <summary>嵌套数据类（public 实例字段递归捕获）。</summary>
        [SaveData("V2Stats", 1)]
        public sealed class V2Stats
        {
            public int Kills;
            public string Checkpoint;
            public List<int> Scores;
        }

        /// <summary>v2 类型矩阵测试组件。</summary>
        internal partial class V2Component : MonoBehaviour
        {
            [SaveField]
            internal List<int> Numbers;

            [SaveField]
            internal Dictionary<string, int> Inventory;

            [SaveField]
            internal Stack<string> UndoStack;

            [SaveField]
            internal int[] Scores;

            [SaveField]
            internal V2Stats Stats;

            [SaveField]
            internal List<V2Stats> History;

            [SaveField]
            internal Transform Target;

            [SaveField]
            internal Texture2D Icon;
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects)
            {
                if (gameObject != null)
                {
                    SaveEntityRegistry.Unregister(gameObject.GetComponent<SaveObjectIdentity>());
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
            foreach (UnityEngine.Object asset in _assets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            _assets.Clear();
        }

        /// <summary>
        /// 创建挂载测试组件的 GameObject。
        /// </summary>
        private V2Component CreateComponent(string name)
        {
            var gameObject = new GameObject("kv2-test-" + name);
            _objects.Add(gameObject);
            return gameObject.AddComponent<V2Component>();
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectWarningLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Warning, new Regex(".*"));
            }
        }

        /// <summary>
        /// 捕获 → 消费外层作用域头 → 恢复（返回目标组件）。
        /// </summary>
        private static void RoundTrip(V2Component source, V2Component target)
        {
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(V2Component), out ISaveComponentCapturer capturer), "SG 应已生成 V2Component 捕获器");
            var allMask = new SaveFieldMask(capturer.FieldNames, new HashSet<string>(capturer.FieldNames));

            var writer = new SaveKeyValueWriter(256);
            capturer.Capture(source, ref writer, allMask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType scopeType), "应有外层作用域记录");
            Assert.AreEqual(ESaveKvType.Object, scopeType);
            capturer.Restore(target, ref reader, reader.ReadChildCount(), allMask);
        }

        [Test]
        public void Capturer_IsRegistered()
        {
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(V2Component), out ISaveComponentCapturer capturer),
                "SaveHost SG 应为测试程序集生成捕获器（失败 = 生成链路回归）");
            Assert.AreEqual(8, capturer.FieldNames.Length);
        }

        [Test]
        public void RoundTrip_CollectionsAndNested()
        {
            V2Component source = CreateComponent("src");
            source.Numbers = new List<int> { 3, 1, 4 };
            source.Inventory = new Dictionary<string, int> { ["potion"] = 5, ["ether"] = 2 };
            source.UndoStack = new Stack<string>(new[] { "bottom", "top" });
            source.Scores = new[] { 10, 20 };
            source.Stats = new V2Stats { Kills = 9, Checkpoint = "cp-2", Scores = new List<int> { 7, 8 } };
            source.History = new List<V2Stats>
            {
                new V2Stats { Kills = 1, Checkpoint = "cp-0", Scores = null },
                null,
                new V2Stats { Kills = 2, Checkpoint = "cp-1", Scores = new List<int>() },
            };

            V2Component target = CreateComponent("dst");
            target.Numbers = new List<int> { 999 };
            RoundTrip(source, target);

            CollectionAssert.AreEqual(new[] { 3, 1, 4 }, target.Numbers);
            Assert.AreEqual(2, target.Inventory.Count);
            Assert.AreEqual(5, target.Inventory["potion"]);
            Assert.AreEqual(2, target.Inventory["ether"]);
            // Stack 捕获按枚举序（顶→底）写出，恢复逆序压栈还原 LIFO
            Assert.AreEqual("top", target.UndoStack.Peek());
            Assert.AreEqual(2, target.UndoStack.Count);
            CollectionAssert.AreEqual(new[] { 10, 20 }, target.Scores);

            Assert.IsNotNull(target.Stats);
            Assert.AreEqual(9, target.Stats.Kills);
            Assert.AreEqual("cp-2", target.Stats.Checkpoint);
            CollectionAssert.AreEqual(new[] { 7, 8 }, target.Stats.Scores);

            Assert.AreEqual(3, target.History.Count);
            Assert.AreEqual(1, target.History[0].Kills);
            Assert.IsNull(target.History[0].Scores, "嵌套对象内的 null 集合应保持 null");
            Assert.IsNull(target.History[1], "null 嵌套元素应保持 null");
            Assert.IsNotNull(target.History[2].Scores, "空集合与 null 应区分");
            Assert.AreEqual(0, target.History[2].Scores.Count);
        }

        [Test]
        public void RoundTrip_NullCollections_RestoreNull()
        {
            V2Component source = CreateComponent("null-src");
            source.Numbers = null;
            source.Stats = null;

            V2Component target = CreateComponent("null-dst");
            target.Numbers = new List<int> { 1 };
            target.Stats = new V2Stats { Kills = 5 };
            RoundTrip(source, target);

            Assert.IsNull(target.Numbers, "null 序列应恢复为 null（覆盖目标现值）");
            Assert.IsNull(target.Stats, "null 嵌套对象应恢复为 null");
        }

        [Test]
        public void RoundTrip_SceneReference_ViaIdentityRegistry()
        {
            var targetGo = new GameObject("kv2-test-enemy");
            _objects.Add(targetGo);
            var identity = targetGo.AddComponent<SaveObjectIdentity>();
            identity.m_Id = "enemy-fixed-id";
            SaveEntityRegistry.Register(identity);

            V2Component source = CreateComponent("ref-src");
            source.Target = targetGo.transform;

            V2Component target = CreateComponent("ref-dst");
            RoundTrip(source, target);

            Assert.AreSame(targetGo.transform, target.Target, "场景引用应经 SaveEntityRegistry 反查还原为同一实例");
        }

        [Test]
        public void RoundTrip_SceneReference_MissingIdentity_WritesNull()
        {
            ExpectWarningLogForUtf();
            V2Component source = CreateComponent("noid-src");
            source.Target = CreateComponent("noid-target").transform; // 无 SaveObjectIdentity

            V2Component target = CreateComponent("noid-dst");
            RoundTrip(source, target);

            Assert.IsNull(target.Target, "目标缺 SaveObjectIdentity 时捕获写 Null");
        }

        [Test]
        public void RoundTrip_AssetReference_ViaCatalog()
        {
            var icon = new Texture2D(2, 2);
            _assets.Add(icon);
            var catalog = ScriptableObject.CreateInstance<SaveAssetCatalog>();
            _assets.Add(catalog);
            catalog.m_Entries.Add(new SaveAssetCatalog.Entry { m_Asset = icon, m_Location = "Assets/Textures/hero.png" });

            // 注入设置实例（仅内存，测试后还原——设置资产为并行改动高发区，不落盘）
            SaveAssetCatalog previous = SaveServiceSettings.AssetCatalog;
            typeof(SaveServiceSettings)
                .GetField("m_AssetCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(SaveServiceSettings.Instance, catalog);
            try
            {
                V2Component source = CreateComponent("asset-src");
                source.Icon = icon;

                V2Component target = CreateComponent("asset-dst");
                RoundTrip(source, target);

                Assert.AreSame(icon, target.Icon, "资产引用应经 SaveAssetCatalog 反查还原为同一实例");
            }
            finally
            {
                typeof(SaveServiceSettings)
                    .GetField("m_AssetCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(SaveServiceSettings.Instance, previous);
            }
        }

        [Test]
        public void RoundTrip_AssetReference_NotInCatalog_WritesNull()
        {
            // 无目录配置（设置资产默认 m_AssetCatalog 为空）时资产引用捕获写 Null
            if (SaveServiceSettings.AssetCatalog != null)
            {
                Assert.Inconclusive("设置资产已配置目录，跳过未登记路径");
                return;
            }

            ExpectWarningLogForUtf();
            var icon = new Texture2D(2, 2);
            _assets.Add(icon);
            V2Component source = CreateComponent("nocat-src");
            source.Icon = icon;

            V2Component target = CreateComponent("nocat-dst");
            RoundTrip(source, target);

            Assert.IsNull(target.Icon, "无目录时资产引用捕获写 Null");
        }
    }
}
