using Moirai.Atropos.Debugger;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 错误日志记录器的启用判据（<see cref="UGUIHandler"/> 的 <c>ShouldEnableErrorLog</c>）单元测试。
    /// <para>判据决定发布包里每次异常是否弹出 <c>LogUI</c>：曾出现过「判据为假时才构造记录器」的反向写法，
    /// 表现为开发包与编辑器静默、发布包反而弹窗。这里把四种窗口策略与两个环境位钉成表，
    /// 纯逻辑测试，不依赖场景、Canvas 与 UI 后端。</para>
    /// </summary>
    [TestFixture]
    public sealed class UGUIHandlerErrorLogPolicyTests
    {
        [Test]
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ShouldEnableErrorLog_AlwaysOpen_IsEnabled(bool isDebugBuild, bool isEditor)
        {
            Assert.IsTrue(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.AlwaysOpen, isDebugBuild, isEditor),
                "总是打开策略下不应依赖环境位");
        }

        [Test]
        public void ShouldEnableErrorLog_OnlyOpenWhenDevelopment_FollowsDebugBuild()
        {
            Assert.IsTrue(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.OnlyOpenWhenDevelopment, true, false),
                "开发构建应启用错误日志");

            // 发布包的默认形态（isDebugBuild=false）——这一格就是反向写法当场露馅的地方
            Assert.IsFalse(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.OnlyOpenWhenDevelopment, false, false),
                "发布构建不应启用错误日志，否则每次异常都会弹窗");
        }

        [Test]
        public void ShouldEnableErrorLog_OnlyOpenInEditor_FollowsEditor()
        {
            Assert.IsTrue(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.OnlyOpenInEditor, false, true),
                "编辑器内应启用错误日志");

            Assert.IsFalse(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.OnlyOpenInEditor, true, false),
                "非编辑器不应启用错误日志");
        }

        [Test]
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ShouldEnableErrorLog_AlwaysClose_IsDisabled(bool isDebugBuild, bool isEditor)
        {
            Assert.IsFalse(UGUIHandler.ShouldEnableErrorLog(
                DebuggerActiveWindowType.AlwaysClose, isDebugBuild, isEditor),
                "总是关闭策略下不得启用错误日志");
        }
    }
}
