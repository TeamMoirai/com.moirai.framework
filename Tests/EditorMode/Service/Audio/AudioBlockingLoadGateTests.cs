using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 阻塞加载门禁契约：启动窗口可同步，首帧 Tick 后强制异步。
    /// </summary>
    [TestFixture]
    public sealed class AudioBlockingLoadGateTests
    {
        [SetUp]
        public void SetUp() => AudioBlockingLoadGate.Open();

        [TearDown]
        public void TearDown() => AudioBlockingLoadGate.Open();

        [Test]
        public void Default_Open_ToAllowBootstrapBlockingLoads()
        {
            AudioBlockingLoadGate.Open();
            Assert.IsTrue(AudioBlockingLoadGate.IsOpen);
        }

        [Test]
        public void Close_BlocksFurtherBlockingLoads()
        {
            AudioBlockingLoadGate.Close();
            Assert.IsFalse(AudioBlockingLoadGate.IsOpen);
        }

        [Test]
        public void Restart_ReopensWindow()
        {
            AudioBlockingLoadGate.Close();
            AudioBlockingLoadGate.Open();
            Assert.IsTrue(AudioBlockingLoadGate.IsOpen, "Restart 必须重新允许启动期阻塞加载");
        }
    }
}
