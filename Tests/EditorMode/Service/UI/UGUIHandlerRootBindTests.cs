using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// <see cref="UGUIHandler.TryBindRoot"/> 的续等语义测试：缺绑定、缺 Canvas 都不得一次性放弃。
    /// <para>钉住三件事：无绑定时挂起且只报一条 Error；有绑定无 Canvas 时挂起且只报一条 Fatal
    /// （Canvas 补上后能续绑成功）；绑定成功后不再续等。刻意不再按名字查找是另一条已钉住的路径。</para>
    /// <para>EditMode 下 <c>DontDestroyOnLoad</c> 会抛，实现侧已有 <c>Application.isPlaying</c> 守卫，
    /// 故成功路径也能在编辑器夹具里走到。</para>
    /// </summary>
    [TestFixture]
    public sealed class UGUIHandlerRootBindTests
    {
        private UGUIHandler _handler;
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_handler != null)
            {
                _handler.Internal_Shutdown();
                _handler = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
                _root = null;
            }

            Assert.IsNull(UIRootBinding.TryGetInstance(), "夹具不得把 UI 根留给下一个用例");
        }

        [Test]
        public void TryBindRoot_NoBinding_StaysAwaitingAndLogsOnce()
        {
            _handler = new UGUIHandler();

            UtfLogExpect.Error();
            _handler.TryBindRoot();
            Assert.IsNull(_handler.UIRoot, "无绑定时不得伪造 UI 根");

            // 第二次：仍在等，但不得再刷一条日志（UtfLogExpect 只声明过一条；多报会判未处理日志）
            _handler.TryBindRoot();
            Assert.IsNull(_handler.UIRoot, "续等期间 UI 根应保持为空");
        }

        [Test]
        public void TryBindRoot_BindingWithoutCanvas_StaysAwaitingAndLogsOnce()
        {
            _root = new GameObject("UIRootNoCanvas");
            var binding = _root.AddComponent<UIRootBinding>();
            binding.Internal_Bind();

            _handler = new UGUIHandler();

            UtfLogExpect.Error();
            _handler.TryBindRoot();
            Assert.IsNull(_handler.UIRoot, "缺 Canvas 时不得绑到空根上");

            _handler.TryBindRoot();
            Assert.IsNull(_handler.UIRoot, "续等期间 UI 根应保持为空");
        }

        [Test]
        public void TryBindRoot_CanvasAddedLater_BindsOnRetry()
        {
            _root = new GameObject("UIRootLateCanvas");
            var binding = _root.AddComponent<UIRootBinding>();
            binding.Internal_Bind();

            _handler = new UGUIHandler();

            UtfLogExpect.Error();
            _handler.TryBindRoot();
            Assert.IsNull(_handler.UIRoot, "前置条件：尚无 Canvas，绑定未完成");

            _root.AddComponent<Canvas>();
            _handler.TryBindRoot();

            Assert.IsNotNull(_handler.UIRoot, "Canvas 补上后下一次续等应绑成功");
            Assert.AreSame(_root.GetComponentInChildren<Canvas>().transform, _handler.UIRoot, "UI 根应指向 Canvas 节点");
        }

        [Test]
        public void TryBindRoot_WithCanvas_BindsImmediately()
        {
            _root = new GameObject("UIRootReady");
            var canvas = _root.AddComponent<Canvas>();
            var binding = _root.AddComponent<UIRootBinding>();
            binding.Internal_Bind();

            _handler = new UGUIHandler();
            _handler.TryBindRoot();

            Assert.AreSame(canvas.transform, _handler.UIRoot, "有绑定又有 Canvas 时应一次绑上");
        }
    }
}
