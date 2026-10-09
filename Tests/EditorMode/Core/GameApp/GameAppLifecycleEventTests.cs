using System;
using System.Collections.Generic;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using App = Moirai.Atropos.GameApp;

namespace Core.GameApp
{
    /// <summary>
    /// <see cref="App"/> 两个生命周期广播的契约：对焦回执逐项送达、单项异常不截断其余订户、注销后即停发。
    /// </summary>
    /// <remarks>
    /// 经 <c>InvokeApplicationFocus</c> / <c>InvokeApplicationQuit</c> 这两个引擎回调入口驱动，与 <c>Application.focusChanged</c> / <br />
    /// <c>Application.quitting</c> 打的是同一道门，不另造测试专用触发器。 <br />
    /// <c>InvokeApplicationQuit</c> 尾随的那次关停靠 <see cref="App.IsShutdown"/> 本值为真而成空转——关停会改写编辑器全局 PlayerLoop <br />
    /// 并关掉真实服务世界，代价大且不可逆，故这一前提在 SetUp 里当闸门断死：前提不成立时本套件整体判红，而不是跑出一场破坏。 <br />
    /// 「先广播后关停」的次序因此不在射程内（关停在此不可观测），要验它得走 PlayMode。
    /// </remarks>
    public class GameAppLifecycleEventTests
    {
        private readonly List<Action<bool>> _focusHandlers = new List<Action<bool>>();
        private readonly List<Action> _quitHandlers = new List<Action>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(App.IsShutdown,
                "GameApp 在本域内是在线态：InvokeApplicationQuit 会真跑关停，本套件的隔离前提不成立");
        }

        private void SubscribeFocus(Action<bool> handler)
        {
            _focusHandlers.Add(handler);
            App.onApplicationFocus += handler;
        }

        private void SubscribeQuit(Action handler)
        {
            _quitHandlers.Add(handler);
            App.onApplicationQuit += handler;
        }

        [TearDown]
        public void TearDown()
        {
            // 用例自持注销：摘不干净等于把订户留给同域内后跑的任何夹具
            for (int i = 0; i < _focusHandlers.Count; i++) App.onApplicationFocus -= _focusHandlers[i];
            for (int i = 0; i < _quitHandlers.Count; i++) App.onApplicationQuit -= _quitHandlers[i];

            _focusHandlers.Clear();
            _quitHandlers.Clear();
        }

        #region 对焦回执 [FOCUS BROADCAST]

        [Test]
        public void ApplicationFocusBroadcast_DeliversEveryValueInOrder()
        {
            var seen = new List<bool>();
            SubscribeFocus(seen.Add);

            App.InvokeApplicationFocus(true);
            App.InvokeApplicationFocus(false);
            App.InvokeApplicationFocus(true);

            Assert.AreEqual(new[] { true, false, true }, seen.ToArray());
        }

        [Test]
        public void ApplicationFocusBroadcast_WhenOneThrows_OthersStillRun()
        {
            // 失焦联动输入是「每个订户各管一份状态」的广播，前序订户抛异常不该让后序静默失去响应机会
            UtfLogExpect.Error();

            var order = new List<string>();
            SubscribeFocus(_ =>
            {
                order.Add("first");
                throw new InvalidOperationException("boom");
            });
            SubscribeFocus(_ => order.Add("second"));

            Assert.DoesNotThrow(() => App.InvokeApplicationFocus(false));

            Assert.AreEqual(new[] { "first", "second" }, order.ToArray());
        }

        [Test]
        public void ApplicationFocusUnsubscribe_StopsDelivery()
        {
            int calls = 0;
            Action<bool> handler = _ => calls++;
            App.onApplicationFocus += handler;

            App.InvokeApplicationFocus(true);
            App.onApplicationFocus -= handler;
            App.InvokeApplicationFocus(true);

            // 同一实例摘一次即止：摘不掉（lambda 每次求值是新实例）与摘多了都是这条格子的红
            Assert.AreEqual(1, calls);
        }

        #endregion

        #region 退出回执 [QUIT BROADCAST]

        [Test]
        public void ApplicationQuitBroadcast_WhenOneThrows_OthersStillRun()
        {
            // 关闭广播的职责就是清理，截断等于静默漏掉后续每一项的释放动作，故开发期也不上抛
            UtfLogExpect.Error();

            var order = new List<string>();
            SubscribeQuit(() =>
            {
                order.Add("first");
                throw new InvalidOperationException("boom");
            });
            SubscribeQuit(() => order.Add("second"));

            Assert.DoesNotThrow(() => App.InvokeApplicationQuit());

            Assert.AreEqual(new[] { "first", "second" }, order.ToArray());
        }

        #endregion
    }
}
