using Moirai.Atropos.Diagnostics;
using NUnit.Framework;

namespace Debugger
{
    public sealed class DebuggerGateTests
    {
        [TestCase(true, true, false, false, true)]
        [TestCase(true, false, true, false, true)]
        [TestCase(true, false, false, true, true)]
        [TestCase(true, false, false, false, false)]
        [TestCase(false, true, true, true, false)]
        [TestCase(false, true, false, false, false)]
        public void Gate_RequiresDevelopmentBuild(bool development, bool command, bool setting,
            bool force, bool expected)
        {
            Assert.AreEqual(expected,
                DebuggerGate.ShouldEnable(development, command, setting, force));
        }
    }
}
