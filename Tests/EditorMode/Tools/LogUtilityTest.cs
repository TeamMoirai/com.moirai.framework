using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Utility
{
    /// <summary>
    /// LogUtility 外观与 LogHandler 抽象的单元测试。
    /// <para>不创建 LogHandler 子类（避免 [SerializeReference] Inspector 下拉污染），
    /// 使用 DefaultLogHandler + OnMessageLogged 事件捕获日志条目。</para>
    /// </summary>
    public class LogUtilityTest
    {
        private List<(LogUtility.ELogLevel Level, string Message, Exception Exception)> _entries;
        private DefaultLogHandler _handler;
        private Action<LogUtility.ELogLevel, string, Exception> _callback;

        [SetUp]
        public void SetUp()
        {
            _entries = new();
            _handler = new DefaultLogHandler { MinimumLevel = LogUtility.ELogLevel.Debug };
            LogUtility.Handler = _handler;

            _callback = (level, msg, ex) => _entries.Add((level, msg, ex));
            LogUtility.OnMessageLogged += _callback;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= _callback;
            LogUtility.ResetStatics();
            LogUtility.Handler = new DefaultLogHandler();
        }

        #region 处理器 [HANDLER]

        [Test]
        public void Handler_DefaultIsCreatedLazily()
        {
            LogUtility.ResetStatics();
            Assert.IsNotNull(LogUtility.Handler);
        }

        [Test]
        public void Handler_SetNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LogUtility.Handler = null);
        }

        [Test]
        public void Handler_Replace_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => LogUtility.Handler = new DefaultLogHandler());
        }

        #endregion

        #region 日志路由 [LOG ROUTING]

        [Test]
        public void Log_Levels_AreRouted()
        {
            LogUtility.Debug("d");
            LogUtility.Info("i");
            LogUtility.Warning("w");
            LogAssert.Expect(LogType.Error, new Regex(@"\[ERROR\].*e"));
            LogUtility.Error("e");

            Assert.AreEqual(4, _entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Debug, _entries[0].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Info, _entries[1].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, _entries[2].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Error, _entries[3].Level);
        }

        [Test]
        public void Log_FilteredLevel_IsSkipped()
        {
            _handler.MinimumLevel = LogUtility.ELogLevel.Warning;

            LogUtility.Debug("d");
            LogUtility.Info("i");
            LogUtility.Warning("w");

            Assert.AreEqual(1, _entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, _entries[0].Level);
        }

        [Test]
        public void Log_FormatOverloads_FormatMessage()
        {
            LogUtility.Info("A={0}, B={1}", 1, "b");
            LogUtility.Warning("C={0} D={1} E={2} F={3} G={4} H={5}", 1, 2, 3, 4, 5, 6);

            Assert.AreEqual("A=1, B=b", _entries[0].Message);
            StringAssert.StartsWith("C=1 D=2 E=3 F=4 G=5 H=6", _entries[1].Message);
        }

        [Test]
        public void Log_ObjectOverload_ConvertsToString()
        {
            LogUtility.Debug(42);
            Assert.AreEqual("42", _entries[0].Message);
        }

        [Test]
        public void Log_NullMessage_BecomesEmpty()
        {
            LogUtility.Info((string)null);
            Assert.AreEqual(1, _entries.Count);
            Assert.AreEqual(string.Empty, _entries[0].Message);
        }

        #endregion

        #region 异常重载 [EXCEPTION OVERLOADS]

        [Test]
        public void Log_ErrorWithException_PassesException()
        {
            var exception = new InvalidOperationException("boom");
            LogAssert.Expect(LogType.Error, new Regex(@"\[ERROR\].*boom"));

            LogUtility.Error(exception);

            Assert.AreEqual(LogUtility.ELogLevel.Error, _entries[0].Level);
            Assert.AreSame(exception, _entries[0].Exception);
            StringAssert.Contains("boom", _entries[0].Message);
        }

        [Test]
        public void Log_FatalWithException_PassesException()
        {
            var exception = new InvalidOperationException("fatal");
            // Fatal + 异常走 Debug.LogException（LogType.Exception）通道
            LogAssert.Expect(LogType.Exception, new Regex(".*fatal.*"));

            LogUtility.Fatal(exception);

            Assert.AreEqual(LogUtility.ELogLevel.Fatal, _entries[0].Level);
            Assert.AreSame(exception, _entries[0].Exception);
        }

        [Test]
        public void DefaultLogHandler_Fatal_DoesNotThrow()
        {
            LogAssert.Expect(LogType.Error, new Regex(".*FATAL.*unrecoverable.*"));
            Assert.DoesNotThrow(() => LogUtility.Fatal("unrecoverable"));
            LogAssert.Expect(LogType.Exception, new Regex(".*boom.*"));
            Assert.DoesNotThrow(() => LogUtility.Fatal(new InvalidOperationException("boom")));
        }

        [Test]
        public void DefaultLogHandler_MinimumLevel_FiltersEntries()
        {
            var handler = new DefaultLogHandler { MinimumLevel = LogUtility.ELogLevel.Error };

            Assert.IsFalse(handler.IsEnabled(LogUtility.ELogLevel.Warning));
            Assert.IsTrue(handler.IsEnabled(LogUtility.ELogLevel.Error));
            Assert.IsTrue(handler.IsEnabled(LogUtility.ELogLevel.Fatal));
        }

        #endregion

        #region 事件回调 [MESSAGE LOGGED EVENT]

        [Test]
        public void MessageLogged_FiresAfterLog()
        {
            LogUtility.Info("hello");
            LogAssert.Expect(LogType.Error, new Regex(@"\[ERROR\].*oops"));
            LogUtility.Error("oops");

            Assert.AreEqual(2, _entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Info, _entries[0].Level);
            Assert.AreEqual("hello", _entries[0].Message);
            Assert.AreEqual(LogUtility.ELogLevel.Error, _entries[1].Level);
            Assert.AreEqual("oops", _entries[1].Message);
        }

        [Test]
        public void MessageLogged_NotFiredWhenFiltered()
        {
            _handler.MinimumLevel = LogUtility.ELogLevel.Warning;
            LogUtility.Debug("filtered");
            LogUtility.Warning("passes");

            Assert.AreEqual(1, _entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, _entries[0].Level);
        }

        [Test]
        public void MessageLogged_ExceptionOverload_FiresWithException()
        {
            var ex = new InvalidOperationException("err");
            LogAssert.Expect(LogType.Error, new Regex(@"\[ERROR\].*err"));

            LogUtility.Error(ex);

            Assert.AreEqual(1, _entries.Count);
            Assert.AreSame(ex, _entries[0].Exception);
        }

        #endregion

        #region 全局拦截 [GLOBAL INTERCEPTION]

        [Test]
        public void GlobalInterception_CanEnableAndDisable()
        {
            LogUtility.EnableGlobalInterception();
            Assert.IsTrue(LogUtility.IsGlobalInterceptionEnabled);

            LogUtility.DisableGlobalInterception();
            Assert.IsFalse(LogUtility.IsGlobalInterceptionEnabled);
        }

        #endregion
    }
}
