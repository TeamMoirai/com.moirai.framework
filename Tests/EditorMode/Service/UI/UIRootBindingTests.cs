using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// <see cref="UIRootBinding"/> 的登记语义测试：UI 根靠组件登记，不再按名字查找。
    /// <para>钉住四件事：登记即成为当前根、当前根让位后清空、销毁次要绑定不得清掉当前根、
    /// 重复登记以后者为准并留下可见错误。</para>
    /// <para>EditMode 下 <c>AddComponent</c> 不触发 <c>Awake</c>/<c>OnDestroy</c>，所以用例直调
    /// <see cref="UIRootBinding.Internal_Bind"/> / <see cref="UIRootBinding.Internal_Unbind"/>——
    /// 那两个方法就是两个回调的全部实质，留出 internal 入口就是为了不必反射私有方法。</para>
    /// <para>后端取用与挂起等待的分支住在 <c>UGUIHandler.TryBindRoot</c>，那要 Canvas 与 play mode 才走得到。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIRootBindingTests
    {
        private GameObject _rootA;
        private GameObject _rootB;
        private UIRootBinding _a;
        private UIRootBinding _b;

        [TearDown]
        public void TearDown()
        {
            if (_b != null) _b.Internal_Unbind();
            if (_a != null) _a.Internal_Unbind();
            if (_rootB != null) UnityEngine.Object.DestroyImmediate(_rootB);
            if (_rootA != null) UnityEngine.Object.DestroyImmediate(_rootA);
            _b = null;
            _a = null;
            _rootB = null;
            _rootA = null;

            Assert.IsNull(UIRootBinding.Current, "夹具不得把 UI 根留给下一个用例");
        }

        [Test]
        public void Bind_BecomesCurrent()
        {
            _rootA = new GameObject(nameof(UIRootBinding));
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();

            Assert.AreSame(_a, UIRootBinding.Current, "登记后应立即成为当前 UI 根");
        }

        [Test]
        public void Unbind_Current_ClearsCurrent()
        {
            _rootA = new GameObject(nameof(UIRootBinding));
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();
            Assert.IsNotNull(UIRootBinding.Current, "前置条件：已登记");

            _a.Internal_Unbind();

            Assert.IsNull(UIRootBinding.Current, "当前根让位后不应留下悬空引用");
        }

        [Test]
        public void Unbind_NonCurrent_KeepsCurrent()
        {
            _rootA = new GameObject("UIRootA");
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();

            _rootB = new GameObject("UIRootB");
            _b = _rootB.AddComponent<UIRootBinding>();
            UtfLogExpect.Error();
            _b.Internal_Bind();
            Assert.AreSame(_b, UIRootBinding.Current, "后置：后登记者在位");

            _a.Internal_Unbind();

            Assert.AreSame(_b, UIRootBinding.Current, "让位的不是当前根时，当前根不得被清掉");
        }

        [Test]
        public void Bind_Duplicate_ReplacesCurrentAndLogsError()
        {
            _rootA = new GameObject("UIRootA");
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();

            _rootB = new GameObject("UIRootB");
            _b = _rootB.AddComponent<UIRootBinding>();
            UtfLogExpect.Error();
            _b.Internal_Bind();

            Assert.AreSame(_b, UIRootBinding.Current, "重复登记以后者为准");
            Assert.AreNotSame(_a, _b, "两次登记应是不同实例");
        }
    }
}
