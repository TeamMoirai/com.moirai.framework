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
    /// LogUtility 门面与 LogHandler 抽象的单元测试。
    /// </summary>
    public class LogUtilityTest
    {
        private sealed class TestLogHandler : LogHandler
        {
            public readonly List<(LogUtility.ELogLevel Level, string Message, Exception Exception)> Entries =
                new();

            public int InitCount;
            public int ShutdownCount;
            public LogUtility.ELogLevel MinimumLevel;

            protected override void OnInit() => InitCount++;
            protected override void OnShutdown() => ShutdownCount++;
            public override bool IsEnabled(LogUtility.ELogLevel logLevel) => logLevel >= MinimumLevel;

            public override void Log(LogUtility.ELogLevel logLevel, string message, Exception exception)
            {
                Entries.Add((logLevel, message, exception));
            }
        }

        private TestLogHandler m_Handler;

        [SetUp]
        public void SetUp()
        {
            m_Handler = new TestLogHandler();
            LogUtility.Handler = m_Handler;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.ResetStatics();
            LogUtility.Handler = new DefaultLogHandler();
        }

        #region 处理器 [HANDLER]

        [Test]
        public void Handler_DefaultIsCreatedLazily()
        {
            Assert.IsNotNull(LogUtility.Handler);
        }

        [Test]
        public void Handler_SetNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LogUtility.Handler = null);
        }

        [Test]
        public void Handler_Replace_InvokesLifecycle()
        {
            var replacement = new TestLogHandler();
            LogUtility.Handler = replacement;
            Assert.AreEqual(1, replacement.InitCount);
            Assert.AreEqual(1, m_Handler.ShutdownCount);
        }

        #endregion

        #region 日志路由 [LOG ROUTING]

        [Test]
        public void Log_Levels_AreRouted()
        {
            LogUtility.Debug("d");
            LogUtility.Info("i");
            LogUtility.Warning("w");
            LogUtility.Error("e");

            Assert.AreEqual(4, m_Handler.Entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Debug, m_Handler.Entries[0].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Info, m_Handler.Entries[1].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, m_Handler.Entries[2].Level);
            Assert.AreEqual(LogUtility.ELogLevel.Error, m_Handler.Entries[3].Level);
        }

        [Test]
        public void Log_FilteredLevel_IsSkipped()
        {
            m_Handler.MinimumLevel = LogUtility.ELogLevel.Warning;

            LogUtility.Debug("d");
            LogUtility.Info("i");
            LogUtility.Warning("w");

            Assert.AreEqual(1, m_Handler.Entries.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, m_Handler.Entries[0].Level);
        }

        [Test]
        public void Log_FormatOverloads_FormatMessage()
        {
            LogUtility.Info("A={0}, B={1}", 1, "b");
            LogUtility.Warning("C={0} D={1} E={2} F={3} G={4} H={5}", 1, 2, 3, 4, 5, 6);

            Assert.AreEqual("A=1, B=b", m_Handler.Entries[0].Message);
            StringAssert.StartsWith("C=1 D=2 E=3 F=4 G=5 H=6", m_Handler.Entries[1].Message);
        }

        [Test]
        public void Log_ObjectOverload_ConvertsToString()
        {
            LogUtility.Debug(42);
            Assert.AreEqual("42", m_Handler.Entries[0].Message);
        }

        [Test]
        public void Log_NullMessage_BecomesEmpty()
        {
            LogUtility.Info((string)null);
            Assert.AreEqual(1, m_Handler.Entries.Count);
            Assert.AreEqual(string.Empty, m_Handler.Entries[0].Message);
        }

        #endregion

        #region 异常重载 [EXCEPTION OVERLOADS]

        [Test]
        public void Log_ErrorWithException_PassesException()
        {
            var exception = new InvalidOperationException("boom");

            LogUtility.Error(exception);

            Assert.AreEqual(LogUtility.ELogLevel.Error, m_Handler.Entries[0].Level);
            Assert.AreSame(exception, m_Handler.Entries[0].Exception);
            StringAssert.Contains("boom", m_Handler.Entries[0].Message);
        }

        [Test]
        public void Log_FatalWithException_PassesException()
        {
            var exception = new InvalidOperationException("fatal");

            LogUtility.Fatal(exception);

            Assert.AreEqual(LogUtility.ELogLevel.Fatal, m_Handler.Entries[0].Level);
            Assert.AreSame(exception, m_Handler.Entries[0].Exception);
        }

        [Test]
        public void DefaultLogHandler_Fatal_DoesNotThrow()
        {
            LogUtility.Handler = new DefaultLogHandler();
            LogAssert.Expect(LogType.Error, new Regex(".*FATAL.*unrecoverable.*"));
            Assert.DoesNotThrow(() => LogUtility.Fatal("unrecoverable"));
            LogAssert.Expect(LogType.Error, new Regex(".*FATAL.*boom.*"));
            Assert.DoesNotThrow(() => LogUtility.Fatal(new InvalidOperationException("boom")));
        }

        [Test]
        public void DefaultLogHandler_MinimumLevel_FiltersEntries()
        {
            var handler = new DefaultLogHandler { MinimumLevel = LogUtility.ELogLevel.Error };
            LogUtility.Handler = handler;

            Assert.IsFalse(handler.IsEnabled(LogUtility.ELogLevel.Warning));
            Assert.IsTrue(handler.IsEnabled(LogUtility.ELogLevel.Error));
            Assert.IsTrue(handler.IsEnabled(LogUtility.ELogLevel.Fatal));
        }

        #endregion

        #region 事件回调 [MESSAGE LOGGED EVENT]

        [Test]
        public void MessageLogged_FiresAfterLog()
        {
            var events = new List<(LogUtility.ELogLevel, string, Exception)>();
            LogUtility.MessageLogged += (level, msg, ex) => events.Add((level, msg, ex));

            LogUtility.Info("hello");
            LogUtility.Error("oops");

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Info, events[0].Item1);
            Assert.AreEqual("hello", events[0].Item2);
            Assert.AreEqual(LogUtility.ELogLevel.Error, events[1].Item1);
            Assert.AreEqual("oops", events[1].Item2);
        }

        [Test]
        public void MessageLogged_NotFiredWhenFiltered()
        {
            var events = new List<(LogUtility.ELogLevel, string, Exception)>();
            LogUtility.MessageLogged += (level, msg, ex) => events.Add((level, msg, ex));

            m_Handler.MinimumLevel = LogUtility.ELogLevel.Warning;
            LogUtility.Debug("filtered");
            LogUtility.Warning("passes");

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(LogUtility.ELogLevel.Warning, events[0].Item1);
        }

        [Test]
        public void MessageLogged_ExceptionOverload_FiresWithException()
        {
            var events = new List<(LogUtility.ELogLevel, string, Exception)>();
            LogUtility.MessageLogged += (level, msg, ex) => events.Add((level, msg, ex));

            var ex = new InvalidOperationException("err");
            LogUtility.Error(ex);

            Assert.AreEqual(1, events.Count);
            Assert.AreSame(ex, events[0].Item3);
        }

        #endregion
    }
}
