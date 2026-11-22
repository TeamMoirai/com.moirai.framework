using System;
using NUnit.Framework;
using Tim = Moirai.Atropos.Timer;

namespace Moirai.Atropos.Tests.EditorMode.Service.Kernel
{
    /// <summary>
    /// 注册期依赖校验失败必须回滚——容器不得残留半注册状态（契约可解析/挂起图残留）。
    /// </summary>
    public sealed class RegistrationRollbackTests
    {
        private sealed class BrokenService : ServiceBase
        {
            protected override void OnInit() { }
            protected override void OnShutdown() { }
        }

        /// <summary>声明了一个未注册依赖的依赖方：运行时注册时依赖校验必须失败。</summary>
        [ServiceDependency(typeof(Tim.ITimerService))]
        private sealed class DependentService : ServiceBase
        {
            protected override void OnInit() { }
            protected override void OnShutdown() { }
        }



        [Test]
        public void ValidateFailure_LeavesContainerClean()
        {
            using (var world = new ServiceWorld())
            {
                // 依赖未注册即注册依赖方：世界未初始化路径是入图（依赖校验推迟到 Initialize）；
                // 运行时注册路径（世界已初始化）才是注册期校验失败回滚的验证面——先初始化世界
                world.Register(EServiceScopeKind.App, new BrokenService());
                world.Initialize();

                // 运行时注册：依赖一个未注册的契约——校验必须失败
                Assert.Throws<GameException>(() =>
                    world.Register(EServiceScopeKind.App, typeof(IDependencyProbe), new DependentService()),
                    "运行时注册的依赖校验必须 fail-fast（ITimerService 契约在本测试 world 未注册）");

                // 回滚断言：失败注册不残留任何可解析条目
                Assert.IsFalse(world.TryGetService<IDependencyProbe>(out _),
                    "校验失败的注册不得残留契约条目");
                Assert.IsFalse(world.TryGetService<DependentService>(out _),
                    "校验失败的注册不得残留实现类型条目");
            }
        }

        /// <summary>依赖探针契约：仅用于校验失败的注册声明。</summary>
        private interface IDependencyProbe { }
    }
}
