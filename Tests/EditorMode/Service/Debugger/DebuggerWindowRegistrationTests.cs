using Dbg = Moirai.Atropos.Debugger;
using NUnit.Framework;

namespace Service.Debugger
{
    /// <summary>
    /// 内置调试窗口的注册时机测试：注册跟着激活走，而不是在 <c>OnInit</c> 里无条件构造 27 个窗体。
    /// <para>生产默认（<see cref="Dbg.DebuggerActiveWindowType.AlwaysClose"/>，或
    /// <see cref="Dbg.DebuggerActiveWindowType.OnlyOpenWhenDevelopment"/> 且非 debug 构建）从不打开调试器，
    /// 这一整段的构造与 <c>Initialize</c> 是纯启动开销。运行期从关切到开时必须补齐，且只补一次。</para>
    /// <para>激活策略经处理器的 internal 覆盖点给定，不改设置资产——那是跨夹具共享的全局配置，
    /// 而编辑器里默认策略恰好落在「激活」一侧，未激活形态不覆盖就测不到。</para>
    /// </summary>
    [TestFixture]
    public sealed class DebuggerWindowRegistrationTests
    {
        [Test]
        public void InternalInit_AlwaysClose_RegistersNoBuiltInWindows()
        {
            var handler = new Dbg.DefaultDebuggerHandler();
            handler.Internal_ActiveWindowTypeOverride = Dbg.DebuggerActiveWindowType.AlwaysClose;
            try
            {
                handler.Internal_Init();

                Assert.IsFalse(handler.ActiveWindow, "前置条件：AlwaysClose 且未带 -showdebugger 时应为未激活");
                Assert.IsNotNull(handler.WindowRegistry, "注册表仍应随处理器初始化建立");
                Assert.AreEqual(0, handler.WindowRegistry.WindowCount, "未激活的构建不该构造任何内置窗口");
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void InternalInit_AlwaysOpen_RegistersBuiltInWindows()
        {
            var handler = new Dbg.DefaultDebuggerHandler();
            handler.Internal_ActiveWindowTypeOverride = Dbg.DebuggerActiveWindowType.AlwaysOpen;
            try
            {
                handler.Internal_Init();

                Assert.IsTrue(handler.ActiveWindow, "前置条件：AlwaysOpen 应为激活");
                Assert.Greater(handler.WindowRegistry.WindowCount, 0, "激活路径必须填内置窗口，懒建不得把窗口懒没了");
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void ActiveWindow_TurningOnRegistersBuiltInWindowsOnce()
        {
            var handler = new Dbg.DefaultDebuggerHandler();
            handler.Internal_ActiveWindowTypeOverride = Dbg.DebuggerActiveWindowType.AlwaysClose;
            try
            {
                handler.Internal_Init();
                Assert.AreEqual(0, handler.WindowRegistry.WindowCount, "前置条件：未激活时注册表为空");

                handler.ActiveWindow = true;
                int count = handler.WindowRegistry.WindowCount;
                Assert.Greater(count, 0, "运行期从关切到开应补齐内置窗口");

                int version = handler.WindowRegistry.Version;
                handler.ActiveWindow = true;
                Assert.AreEqual(count, handler.WindowRegistry.WindowCount, "重复开启不得再注册一轮");
                Assert.AreEqual(version, handler.WindowRegistry.Version, "重复开启不得推进注册表版本");
            }
            finally
            {
                handler.ActiveWindow = false;
                handler.Internal_Shutdown();
            }
        }
    }
}
