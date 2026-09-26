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

        [Test]
        public void PathPlay_DefaultsToAsync()
        {
            var byRefOptions = typeof(AudioPlayOptions).MakeByRefType();
            var signature = new[] { typeof(string), byRefOptions, typeof(bool), typeof(bool) };

            var facade = typeof(AudioService).GetMethod("Play", signature);
            Assert.IsNotNull(facade);
            Assert.AreEqual(true, facade.GetParameters()[2].DefaultValue,
                "路径播放必须默认异步");

            var contract = typeof(AudioServiceHandler).GetMethod("Play", signature);
            Assert.IsNotNull(contract);
            Assert.AreEqual(true, contract.GetParameters()[2].DefaultValue,
                "抽象契约默认异步，与外观一致");
        }
    }
}
