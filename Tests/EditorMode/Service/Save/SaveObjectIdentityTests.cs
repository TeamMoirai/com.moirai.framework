using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Save
{
    /// <summary>
    /// 场景对象身份与实体注册表测试：注册/注销/反查、空 ID 拒注册、重复 ID 首到先得、销毁后反查失效、Resolve 解析。
    /// <para>告警断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// UTF 可见链路另补 <c>LogAssert.Expect</c>（黑名单：is not UnityLoggingHandler）。</para>
    /// </summary>
    public class SaveObjectIdentityTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            foreach (GameObject gameObject in _objects)
            {
                if (gameObject != null)
                {
                    SaveEntityRegistry.Unregister(gameObject.GetComponent<SaveObjectIdentity>());
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
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
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Warning, new Regex(".*"));
            }
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
        /// 创建挂载身份组件的 GameObject（直接赋 ID，绕开编辑器 OnValidate 烘焙）。
        /// </summary>
        private SaveObjectIdentity CreateIdentity(string name, string id)
        {
            var gameObject = new GameObject("identity-test-" + name);
            _objects.Add(gameObject);
            var identity = gameObject.AddComponent<SaveObjectIdentity>();
            identity.m_Id = id;
            return identity;
        }

        [Test]
        public void Register_ThenTryFind_ReturnsInstance()
        {
            SaveObjectIdentity identity = CreateIdentity("a", "id-a");
            SaveEntityRegistry.Register(identity);

            Assert.IsTrue(SaveEntityRegistry.TryFind("id-a", out SaveObjectIdentity found));
            Assert.AreSame(identity, found);
        }

        [Test]
        public void Unregister_ThenTryFind_Misses()
        {
            SaveObjectIdentity identity = CreateIdentity("b", "id-b");
            SaveEntityRegistry.Register(identity);
            SaveEntityRegistry.Unregister(identity);

            Assert.IsFalse(SaveEntityRegistry.TryFind("id-b", out SaveObjectIdentity found));
            Assert.IsNull(found);
        }

        [Test]
        public void Register_EmptyId_SkippedWithWarning()
        {
            ExpectWarningLogForUtf();
            SaveObjectIdentity identity = CreateIdentity("empty", string.Empty);
            SaveEntityRegistry.Register(identity);

            Assert.IsFalse(SaveEntityRegistry.TryFind(string.Empty, out _));
            AssertWarningLogged("empty id");
        }

        [Test]
        public void Register_DuplicateId_FirstWinsWithWarning()
        {
            ExpectWarningLogForUtf();
            SaveObjectIdentity first = CreateIdentity("first", "id-dup");
            SaveObjectIdentity second = CreateIdentity("second", "id-dup");

            SaveEntityRegistry.Register(first);
            SaveEntityRegistry.Register(second);

            Assert.IsTrue(SaveEntityRegistry.TryFind("id-dup", out SaveObjectIdentity found));
            Assert.AreSame(first, found, "重复 ID 首到先得");
            AssertWarningLogged("Duplicate SaveObjectIdentity id");

            // 后来者销毁不得移除首注册者的登记
            SaveEntityRegistry.Unregister(second);
            Assert.IsTrue(SaveEntityRegistry.TryFind("id-dup", out SaveObjectIdentity still));
            Assert.AreSame(first, still);
        }

        [Test]
        public void TryFind_NullOrWhitespace_Misses()
        {
            Assert.IsFalse(SaveEntityRegistry.TryFind(null, out _));
            Assert.IsFalse(SaveEntityRegistry.TryFind(string.Empty, out _));
            Assert.IsFalse(SaveEntityRegistry.TryFind("   ", out _));
        }

        [Test]
        public void TryFind_AfterDestroy_Misses()
        {
            SaveObjectIdentity identity = CreateIdentity("gone", "id-gone");
            SaveEntityRegistry.Register(identity);

            UnityEngine.Object.DestroyImmediate(identity.gameObject);
            _objects.RemoveAll(item => item == null);

            // 表内残留已销毁对象（绕过 OnDestroy 直注册场景）：TryFind 按 Unity 假 null 判未命中
            Assert.IsFalse(SaveEntityRegistry.TryFind("id-gone", out SaveObjectIdentity found));
            Assert.IsTrue(found == null);
        }

        [Test]
        public void Resolve_ReturnsComponentAndNullSafe()
        {
            SaveObjectIdentity identity = CreateIdentity("resolve", "id-resolve");

            Assert.AreSame(identity, SaveObjectIdentity.Resolve(identity.transform));
            Assert.AreSame(identity, SaveObjectIdentity.Resolve(identity.gameObject));
            Assert.IsNull(SaveObjectIdentity.Resolve((Component)null));
            Assert.IsNull(SaveObjectIdentity.Resolve((GameObject)null));

            var bare = new GameObject("identity-test-bare");
            _objects.Add(bare);
            Assert.IsNull(SaveObjectIdentity.Resolve(bare), "未挂载身份组件的物体应解析为 null");
        }

        [Test]
        public void Id_ExposesSerializedValue()
        {
            SaveObjectIdentity identity = CreateIdentity("prop", "id-prop");
            Assert.AreEqual("id-prop", identity.Id);
        }
    }
}
