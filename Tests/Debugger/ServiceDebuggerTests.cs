using System.Collections.Generic;
using Aud = Moirai.Atropos.Audio;
using Dbg = Moirai.Atropos.Debugger;
using Loc = Moirai.Atropos.Localization;
using Moirai.Atropos;
using NUnit.Framework;
using Proc = Moirai.Atropos.Procedure;
using Res = Moirai.Atropos.Resource;
using Tim = Moirai.Atropos.Timer;
using UnityEngine;

namespace Debugger
{
    /// <summary>
    /// 服务调试器架构测试：IMGUI 视图适配契约、各服务调试视图生命周期与游戏内调试器注册表接入。
    /// <para>ServiceDebuggerComponent（Inspector 宿主组件）已弃用移除——服务调试视图统一经各服务 OnInit 注册进游戏内调试器。</para>
    /// </summary>
    public sealed class ServiceDebuggerTests
    {
        #region 测试桩 [TEST FAKES]

        private sealed class TestServiceDebugView : Dbg.ServiceDebugView
        {
            public override string Title => "Test Service";

            public override bool IsReady => true;

            protected override void OnDrawContent()
            {
            }
        }

        private sealed class TestWindow : Dbg.IDebuggerWindow
        {
            public bool Initialized
            {
                get;
                private set;
            }

            public bool ShuttedDown
            {
                get;
                private set;
            }

            public void Initialize(params object[] args)
            {
                Initialized = true;
            }

            public void Shutdown()
            {
                ShuttedDown = true;
            }

            public void OnEnter()
            {
            }

            public void OnLeave()
            {
            }

            public void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
            }

            public UnityEngine.UIElements.VisualElement CreateView()
            {
                return new UnityEngine.UIElements.Label("Test Window");
            }
        }

        #endregion

        #region 架构锁 [ARCHITECTURE LOCKS]

        [Test]
        public void Architecture_ServiceDebugViewImplementsDebuggerWindow()
        {
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Dbg.ServiceDebugView)),
                "ServiceDebugView 应实现 IDebuggerWindow（IMGUI 视图经适配器接入游戏内调试器）");
        }

        [Test]
        public void Architecture_ServiceDebugView_AdapterExposesWrappedView()
        {
            var view = new TestServiceDebugView();
            var adapter = new Dbg.IMGUIDebuggerWindow(view);

            Assert.AreSame(view, adapter.View, "适配器应暴露被包装的视图实例");
        }

        [Test]
        public void Architecture_ServiceViews_ImplementIDebuggerWindow()
        {
            // 各服务模块的调试视图（放置于对应模块目录）均实现 IDebuggerWindow——OnInit 注册进游戏内调试器
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Tim.TimerServiceDebugView)), "TimerServiceDebugView");
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Res.ResourceServiceDebugView)), "ResourceServiceDebugView");
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Aud.AudioServiceDebugView)), "AudioServiceDebugView");
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Proc.ProcedureServiceDebugView)), "ProcedureServiceDebugView");
            Assert.IsTrue(typeof(Dbg.IDebuggerWindow).IsAssignableFrom(typeof(Loc.LocalizationServiceDebugView)), "LocalizationServiceDebugView");
        }

        #endregion

        #region 视图契约 [VIEW CONTRACT]

        [Test]
        public void TimerView_WindowLifecycleMethods_DoNotThrow()
        {
            Tim.TimerServiceDebugView view = new Tim.TimerServiceDebugView();

            Assert.DoesNotThrow(() =>
            {
                view.Initialize();
                view.OnEnter();
                view.OnUpdate(0.016f, 0.016f);
                view.OnLeave();
                view.Shutdown();
            }, "服务未就绪时生命周期方法应安全跳过");
        }

        [Test]
        public void TimerView_CreateView_ReturnsThemedElementWhenServiceUnready()
        {
            Tim.TimerServiceDebugView view = new Tim.TimerServiceDebugView();

            UnityEngine.UIElements.VisualElement element = view.CreateView();

            Assert.IsNotNull(element, "服务未就绪时也应返回可挂载的提示视图");
        }

        [Test]
        public void ServiceViews_CreateView_ReturnNonNullWhenServiceUnready()
        {
            // EditMode 下服务未初始化——各视图应返回“未就绪”提示视图而非抛异常
            UnityEngine.UIElements.VisualElement resourceView = new Res.ResourceServiceDebugView().CreateView();
            UnityEngine.UIElements.VisualElement audioView = new Aud.AudioServiceDebugView().CreateView();
            UnityEngine.UIElements.VisualElement procedureView = new Proc.ProcedureServiceDebugView().CreateView();
            UnityEngine.UIElements.VisualElement localizationView = new Loc.LocalizationServiceDebugView().CreateView();
            UnityEngine.UIElements.VisualElement gameAppView = new Dbg.GameAppInformationWindow().CreateView();

            Assert.IsNotNull(resourceView, "ResourceServiceDebugView");
            Assert.IsNotNull(audioView, "AudioServiceDebugView");
            Assert.IsNotNull(procedureView, "ProcedureServiceDebugView");
            Assert.IsNotNull(localizationView, "LocalizationServiceDebugView");
            Assert.IsNotNull(gameAppView, "GameAppInformationWindow");
        }

        [Test]
        public void View_WindowLifecycleMethods_DoNotThrow()
        {
            Dbg.ServiceDebugView view = new TestServiceDebugView();

            Assert.DoesNotThrow(() =>
            {
                view.Initialize();
                view.OnEnter();
                view.OnUpdate(0.016f, 0.016f);
                view.OnLeave();
                view.Shutdown();
            });
        }

        [Test]
        public void View_CreateView_WrapsIMGUIContentInUIToolkitElement()
        {
            Dbg.ServiceDebugView view = new TestServiceDebugView();

            UnityEngine.UIElements.VisualElement element = view.CreateView();

            Assert.IsNotNull(element, "ServiceDebugView 默认 CreateView 应返回可挂载的 UI Toolkit 视图");
        }

        #endregion

        #region 游戏内调试器接入 [IN-GAME DEBUGGER INTEGRATION]

        [Test]
        public void DebuggerService_RegisterServiceDebugView_RoundTrip()
        {
            // 显式注册 DebuggerService（组合根模式——显式注册同时重开可能处于关闭态的服务世界）
            GameServices.RegisterService(EServiceScopeKind.App, new Dbg.DebuggerService());

            var view = new TestServiceDebugView();
            const string path = "UnitTest/Service Debug View";
            try
            {
                Dbg.DebuggerService.RegisterDebugView(path, view);

                Assert.AreSame(view, Dbg.DebuggerService.GetDebuggerWindow(path) is Dbg.IMGUIDebuggerWindow adapter ? adapter.View : null,
                    "注册后应能按路径取回经 IMGUI 适配器包装的同一视图");
                Assert.IsTrue(Dbg.DebuggerService.SelectDebuggerWindow(path), "应能选中注册的调试视图");
                Dbg.DebuggerWindowRegistry registry = Dbg.DebuggerService.WindowRegistry;
                Assert.IsNotNull(registry, "注册表应可经外观访问");
                Assert.AreSame(Dbg.DebuggerService.GetDebuggerWindow(path), registry.SelectedWindow, "选中后注册表当前窗口应为注册的视图");
            }
            finally
            {
                Assert.IsTrue(Dbg.DebuggerService.UnregisterDebuggerWindow(path), "解除注册应成功");
                Assert.IsNull(Dbg.DebuggerService.GetDebuggerWindow(path), "解除注册后不应再取到视图");
            }

            // 清理：注销服务，避免污染同域的后续测试
            Assert.IsTrue(
                GameServices.UnregisterService(EServiceScopeKind.App, typeof(Dbg.DebuggerService)),
                "注销 DebuggerService 应成功");
            Assert.IsNull(GameServices.GetService<Dbg.DebuggerService>(), "注销后不应再解析到服务");
        }

        [Test]
        public void DebuggerService_RegisterPanel_RoundTrip()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new Dbg.DebuggerService());

            const string path = "UnitTest/Builder Panel";
            try
            {
                Dbg.DebuggerService.RegisterPanel(path, builder => builder.AddLabel("Hello"));

                Dbg.IDebuggerWindow window = Dbg.DebuggerService.GetDebuggerWindow(path);
                Assert.IsInstanceOf<Dbg.DebugPanel>(window, "RegisterPanel 应注册 DebugPanel 窗口");
            }
            finally
            {
                Dbg.DebuggerService.UnregisterDebuggerWindow(path);
                GameServices.UnregisterService(EServiceScopeKind.App, typeof(Dbg.DebuggerService));
            }
        }

        [Test]
        public void DebuggerService_UnregisterUnknownPath_ReturnsFalse()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new Dbg.DebuggerService());
            try
            {
                Assert.IsFalse(Dbg.DebuggerService.UnregisterDebuggerWindow("UnitTest/NotRegistered"),
                    "解除未注册路径应返回 false");
            }
            finally
            {
                GameServices.UnregisterService(EServiceScopeKind.App, typeof(Dbg.DebuggerService));
            }
        }

        [Test]
        public void DebuggerService_Unregister_CallsWindowShutdown()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new Dbg.DebuggerService());
            var window = new TestWindow();
            const string path = "UnitTest/ShutdownContract";
            try
            {
                Dbg.DebuggerService.RegisterDebuggerWindow(path, window);
                Assert.IsTrue(window.Initialized, "注册时应调用 Initialize");
                Assert.IsFalse(window.ShuttedDown, "注册时不应调用 Shutdown");
            }
            finally
            {
                Assert.IsTrue(Dbg.DebuggerService.UnregisterDebuggerWindow(path), "解除注册应成功");
            }

            Assert.IsTrue(window.ShuttedDown, "解除注册时应调用 Shutdown");
            GameServices.UnregisterService(EServiceScopeKind.App, typeof(Dbg.DebuggerService));
        }

        #endregion
    }
}
