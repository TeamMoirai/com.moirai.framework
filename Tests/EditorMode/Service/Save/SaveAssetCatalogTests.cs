using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// 资产引用目录测试：双向查找、类型不匹配未命中、重复条目首到先得、无效条目跳过、编辑器期查找表失效重建、
    /// 程序化改条目后的 <see cref="SaveAssetCatalog.InvalidateLookup"/> 契约。
    /// <para>告警断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// UTF 可见链路的 <c>LogAssert.Expect</c> 由 <see cref="UtfLogExpect"/> 统一声明。</para>
    /// </summary>
    public class SaveAssetCatalogTests
    {
        private SaveAssetCatalog _catalog;
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<SaveAssetCatalog>();
            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            foreach (UnityEngine.Object asset in _assets)
            {
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }

            _assets.Clear();
            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
            }
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectWarningLogForUtf()
        {
            UtfLogExpect.Warning();
        }

        /// <summary>
        /// 断言已记录包含指定片段的 Warning 日志。
        /// </summary>
        private void AssertWarningLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Warning && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Warning 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 创建可销毁的测试资产。
        /// </summary>
        private T CreateAsset<T>() where T : UnityEngine.Object
        {
            UnityEngine.Object asset = typeof(T) == typeof(Texture2D)
                ? new Texture2D(2, 2)
                : ScriptableObject.CreateInstance(typeof(T));
            _assets.Add(asset);
            return (T)asset;
        }

        /// <summary>
        /// 登记条目。
        /// </summary>
        private void AddEntry(UnityEngine.Object asset, string location)
        {
            _catalog.m_Entries.Add(new SaveAssetCatalog.Entry { m_Asset = asset, m_Location = location });
        }

        [Test]
        public void TryGetLocation_Registered_ReturnsLocation()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "Assets/Textures/hero.png");

            Assert.IsTrue(_catalog.TryGetLocation(texture, out string location));
            Assert.AreEqual("Assets/Textures/hero.png", location);
        }

        [Test]
        public void TryGetLocation_Unregistered_Misses()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            Assert.IsFalse(_catalog.TryGetLocation(texture, out string location));
            Assert.IsNull(location);
        }

        [Test]
        public void TryGetLocation_Null_Misses()
        {
            Assert.IsFalse(_catalog.TryGetLocation(null, out string location));
            Assert.IsNull(location);
        }

        [Test]
        public void TryResolve_Registered_ReturnsAsset()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "Assets/Textures/hero.png");

            Assert.IsTrue(_catalog.TryResolve("Assets/Textures/hero.png", out Texture2D resolved));
            Assert.AreSame(texture, resolved);
        }

        [Test]
        public void TryResolve_TypeMismatch_Misses()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "Assets/Textures/hero.png");

            Assert.IsFalse(_catalog.TryResolve("Assets/Textures/hero.png", out Material resolved), "登记类型与期望类型不符应按未命中处理");
            Assert.IsNull(resolved);
        }

        [Test]
        public void TryResolve_NullOrEmptyLocation_Misses()
        {
            Assert.IsFalse(_catalog.TryResolve<Texture2D>(null, out _));
            Assert.IsFalse(_catalog.TryResolve<Texture2D>(string.Empty, out _));
        }

        [Test]
        public void Lookup_DuplicateAsset_FirstWinsWithWarning()
        {
            ExpectWarningLogForUtf();
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "first.png");
            AddEntry(texture, "second.png");

            Assert.IsTrue(_catalog.TryGetLocation(texture, out string location));
            Assert.AreEqual("first.png", location, "重复资产首到先得");
            AssertWarningLogged("duplicate asset entry");
        }

        [Test]
        public void Lookup_DuplicateLocation_FirstWinsWithWarning()
        {
            ExpectWarningLogForUtf();
            Texture2D first = CreateAsset<Texture2D>();
            Texture2D second = CreateAsset<Texture2D>();
            AddEntry(first, "same.png");
            AddEntry(second, "same.png");

            Assert.IsTrue(_catalog.TryResolve("same.png", out Texture2D resolved));
            Assert.AreSame(first, resolved, "重复定位串首到先得");
            AssertWarningLogged("duplicate location");
        }

        [Test]
        public void Lookup_InvalidEntries_Skipped()
        {
            AddEntry(null, "no-asset.png");
            AddEntry(CreateAsset<Texture2D>(), string.Empty);

            Assert.AreEqual(2, _catalog.Count, "条目仍在清单中（仅查找跳过）");
            Assert.IsFalse(_catalog.TryResolve<Texture2D>("no-asset.png", out _));
        }

        [Test]
        public void OnValidate_InvalidatesLookupCache()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "cached.png");
            Assert.IsTrue(_catalog.TryGetLocation(texture, out _));

            _catalog.m_Entries.Clear();
            AddEntry(texture, "rebuilt.png");
            // 模拟编辑器修改资产触发的 OnValidate（私有方法，反射调用）
            typeof(SaveAssetCatalog)
                .GetMethod("OnValidate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(_catalog, null);

            Assert.IsTrue(_catalog.TryGetLocation(texture, out string location));
            Assert.AreEqual("rebuilt.png", location, "OnValidate 后查找表应重建并反映最新条目");
        }

        [Test]
        public void ProgrammaticAdd_WithoutInvalidate_StaleCacheMissesNewEntry()
        {
            // 锁定契约：m_Entries 直改不会同步失效查找缓存——程序化工具必须调用 InvalidateLookup
            Texture2D first = CreateAsset<Texture2D>();
            Texture2D second = CreateAsset<Texture2D>();
            AddEntry(first, "first.png");
            Assert.IsTrue(_catalog.TryGetLocation(first, out _), "预热查找缓存");

            AddEntry(second, "second.png");
            Assert.IsFalse(_catalog.TryGetLocation(second, out _),
                "缓存未失效时新条目不可见（收集器不得依赖 TryGetLocation 做扫描期去重）");
        }

        [Test]
        public void InvalidateLookup_AfterProgrammaticAdd_RevealsNewEntry()
        {
            Texture2D first = CreateAsset<Texture2D>();
            Texture2D second = CreateAsset<Texture2D>();
            AddEntry(first, "first.png");
            Assert.IsTrue(_catalog.TryGetLocation(first, out _));

            AddEntry(second, "second.png");
            _catalog.InvalidateLookup();

            Assert.IsTrue(_catalog.TryGetLocation(second, out string location), "InvalidateLookup 后应重建并命中新条目");
            Assert.AreEqual("second.png", location);
            Assert.IsTrue(_catalog.TryGetLocation(first, out string firstLocation), "既有条目在重建后仍可见");
            Assert.AreEqual("first.png", firstLocation);
        }

        [Test]
        public void InvalidateLookup_RemovedEntry_NoLongerResolves()
        {
            Texture2D texture = CreateAsset<Texture2D>();
            AddEntry(texture, "hero.png");
            Assert.IsTrue(_catalog.TryGetLocation(texture, out _));

            _catalog.m_Entries.Clear();
            _catalog.InvalidateLookup();

            Assert.IsFalse(_catalog.TryGetLocation(texture, out _), "失效重建后已移除条目不得再命中");
            Assert.IsFalse(_catalog.TryResolve<Texture2D>("hero.png", out _));
        }
    }
}
