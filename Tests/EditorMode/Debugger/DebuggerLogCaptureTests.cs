using System.Collections.Generic;
using Dbg = Moirai.Atropos.Debugger;
using NUnit.Framework;
using UnityEngine;

namespace Debugger
{
    /// <summary>
    /// 调试器日志捕获测试：环形缓冲、增量计数、容量淘汰与最近日志检索。
    /// </summary>
    public sealed class DebuggerLogCaptureTests
    {
        #region 常量 [CONSTANTS]

        private const string TEST_LOG_PREFIX = "[DebuggerLogCaptureTests]";

        #endregion

        #region 生命周期 [LIFECYCLE]

        [TearDown]
        public void TearDown()
        {
            // 测试产生的日志留给各自捕获器；无需全局清理（捕获器 Stop 已退订）。
        }

        #endregion

        #region 捕获与排空 [CAPTURE AND DRAIN]

        [Test]
        public void Capture_UnityLogs_AfterDrainAppearInBuffer()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(16);
            capture.Start();
            try
            {
                Debug.Log(TEST_LOG_PREFIX + " info message");

                Assert.AreEqual(0, capture.Count, "排空前日志应停留在并发队列");
                capture.Drain();

                Assert.AreEqual(1, capture.Count, "排空后日志应进入环形缓冲");
                Assert.AreEqual(1, capture.InfoCount, "Info 计数应增量维护");
                Assert.AreEqual(0, capture.WarningCount);
                Assert.AreEqual(0, capture.ErrorCount);
                Assert.AreEqual(0, capture.FatalCount);
            }
            finally
            {
                capture.Stop();
            }
        }

        [Test]
        public void Capture_SeverityCounts_TrackLogTypes()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(16);
            capture.Start();
            try
            {
                Debug.Log(TEST_LOG_PREFIX + " info");
                Debug.LogWarning(TEST_LOG_PREFIX + " warning");
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*"));
                Debug.LogError(TEST_LOG_PREFIX + " error");
                capture.Drain();

                Assert.AreEqual(1, capture.InfoCount);
                Assert.AreEqual(1, capture.WarningCount);
                Assert.AreEqual(1, capture.ErrorCount);
                Assert.AreEqual(0, capture.FatalCount, "Exception 才计入 Fatal");
            }
            finally
            {
                capture.Stop();
            }
        }

        [Test]
        public void Capture_InvalidCapacity_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() => new Dbg.DebuggerLogCapture(0),
                "容量须为正");
        }

        #endregion

        #region 容量淘汰 [CAPACITY EVICTION]

        [Test]
        public void Capture_EvictsOldest_WhenCapacityExceeded()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(2);
            capture.Start();
            try
            {
                Debug.Log(TEST_LOG_PREFIX + " first");
                Debug.Log(TEST_LOG_PREFIX + " second");
                Debug.Log(TEST_LOG_PREFIX + " third");
                capture.Drain();

                Assert.AreEqual(2, capture.Count, "超出容量的最旧日志应被淘汰");

                List<Dbg.LogNode> logs = new List<Dbg.LogNode>();
                capture.GetRecentLogs(logs);
                Assert.AreEqual(2, logs.Count);
                StringAssert.DoesNotContain("first", logs[0].LogMessage, "最旧的日志应已淘汰");
                StringAssert.Contains("second", logs[0].LogMessage);
                StringAssert.Contains("third", logs[1].LogMessage);
                Assert.AreEqual(2, capture.InfoCount, "淘汰后计数应同步递减");
            }
            finally
            {
                capture.Stop();
            }
        }

        #endregion

        #region 最近日志检索 [RECENT LOG QUERY]

        [Test]
        public void GetRecentLogs_WithCount_ReturnsLatestN()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(16);
            capture.Start();
            try
            {
                Debug.Log(TEST_LOG_PREFIX + " one");
                Debug.Log(TEST_LOG_PREFIX + " two");
                Debug.Log(TEST_LOG_PREFIX + " three");
                capture.Drain();

                List<Dbg.LogNode> logs = new List<Dbg.LogNode>();
                capture.GetRecentLogs(logs, 2);

                Assert.AreEqual(2, logs.Count, "应只返回最近 2 条");
                StringAssert.Contains("two", logs[0].LogMessage);
                StringAssert.Contains("three", logs[1].LogMessage);
            }
            finally
            {
                capture.Stop();
            }
        }

        [Test]
        public void Clear_EmptiesBufferAndCounts()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(16);
            capture.Start();
            try
            {
                Debug.Log(TEST_LOG_PREFIX + " info");
                Debug.LogWarning(TEST_LOG_PREFIX + " warning");
                capture.Drain();
                Assert.AreEqual(2, capture.Count);

                capture.Clear();

                Assert.AreEqual(0, capture.Count);
                Assert.AreEqual(0, capture.InfoCount);
                Assert.AreEqual(0, capture.WarningCount);
            }
            finally
            {
                capture.Stop();
            }
        }

        [Test]
        public void Version_IncrementsOnContentChange()
        {
            Dbg.DebuggerLogCapture capture = new Dbg.DebuggerLogCapture(16);
            capture.Start();
            try
            {
                int initial = capture.Version;
                capture.Drain();
                Assert.AreEqual(initial, capture.Version, "无待排空日志时版本不变");

                Debug.Log(TEST_LOG_PREFIX + " version bump");
                capture.Drain();
                Assert.Greater(capture.Version, initial, "新日志入环应递增版本");

                capture.Clear();
                Assert.Greater(capture.Version, initial + 1, "清空应递增版本");
            }
            finally
            {
                capture.Stop();
            }
        }

        #endregion
    }
}
