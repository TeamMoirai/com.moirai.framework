using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Resource
{
    /// <summary>
    /// 资源服务驱动编排回归测试：卸载调度与过期预算的纯函数决策矩阵，
    /// 以及未接线状态下 DriveTeardown 的幂等安全性。
    /// </summary>
    public sealed class ResourceDriveDecisionTests
    {
        #region 卸载调度决策 [UNLOAD SCHEDULING]

        [Test]
        public void ShouldUnload_NoOperation_ExpiredMaxInterval_Triggers()
        {
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: false,
                elapsedSinceLastUnload: 300f,
                forceRequested: false,
                preorderRequested: false,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsTrue(should);
        }

        [Test]
        public void ShouldUnload_BeforeMinInterval_WithoutRequests_Waits()
        {
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: false,
                elapsedSinceLastUnload: 10f,
                forceRequested: false,
                preorderRequested: false,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsFalse(should);
        }

        [Test]
        public void ShouldUnload_PreorderPastMinInterval_Triggers()
        {
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: false,
                elapsedSinceLastUnload: 61f,
                forceRequested: false,
                preorderRequested: true,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsTrue(should);
        }

        [Test]
        public void ShouldUnload_PreorderBeforeMinInterval_Waits()
        {
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: false,
                elapsedSinceLastUnload: 59f,
                forceRequested: false,
                preorderRequested: true,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsFalse(should);
        }

        [Test]
        public void ShouldUnload_ForceRequest_BypassesIntervals()
        {
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: false,
                elapsedSinceLastUnload: 0f,
                forceRequested: true,
                preorderRequested: false,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsTrue(should);
        }

        [Test]
        public void ShouldUnload_OperationInFlight_BlocksEvenForce()
        {
            // 在途系统卸载未完成时必须串行等待——强制请求同样不能并发触发。
            bool should = ResourceService.ShouldUnloadUnusedAssets(
                operationInFlight: true,
                elapsedSinceLastUnload: 9999f,
                forceRequested: true,
                preorderRequested: true,
                minInterval: 60f,
                maxInterval: 300f);

            Assert.IsFalse(should);
        }

        #endregion

        #region 过期处理预算 [EXPIRY BUDGET]

        [Test]
        public void ResolveExpireCount_NormalFrame_UsesPerFrameQuota()
        {
            int count = ResourceService.ResolveExpireProcessCount(false, 16, 256);

            Assert.AreEqual(16, count);
        }

        [Test]
        public void ResolveExpireCount_UnloadingFrame_BoostsToWhenUnloading()
        {
            int count = ResourceService.ResolveExpireProcessCount(true, 16, 256);

            Assert.AreEqual(256, count);
        }

        [Test]
        public void ResolveExpireCount_NeverBelowPerFrameQuota()
        {
            // 配置倒挂（whenUnloading < perFrame）时仍保证常态配额下限。
            int count = ResourceService.ResolveExpireProcessCount(true, 64, 8);

            Assert.AreEqual(64, count);
        }

        #endregion

        #region 接线安全 [WIRING SAFETY]

        [Test]
        public void DriveTeardown_WithoutWiring_IsIdempotentNoOp()
        {
            // 未接线（s_DriveWired=false）时必须提前返回且不触碰 UpdateDriver/Application 回调。
            Assert.DoesNotThrow(ResourceService.DriveTeardown);
            Assert.DoesNotThrow(ResourceService.DriveTeardown);
        }

        #endregion
    }
}
