using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// <see cref="UIRootBinding"/> 的单例登记语义测试：UI 根靠组件登记，不再按名字查找。
    /// <para>钉住四件事：登记即成为当前根、当前根销毁后清空、先到先得（后到者不得抢位）、
    /// <c>TryGetInstance</c> 只回读不自动创建（场景没有就是没有）。</para>
    /// <para>EditMode 下 <c>AddComponent</c> 不触发 <c>Awake</c>，所以用例直调
    /// <see cref="UIRootBinding.Internal_Bind"/>——那是基类 <c>CheckMultipleInstance</c> 的同一入口。</para>
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
            if (_rootB != null) UnityEngine.Object.DestroyImmediate(_rootB);
            if (_rootA != null) UnityEngine.Object.DestroyImmediate(_rootA);
            _b = null;
            _a = null;
            _rootB = null;
            _rootA = null;

            Assert.IsNull(UIRootBinding.TryGetInstance(), "夹具不得把 UI 根留给下一个用例");
        }

        [Test]
        public void Bind_BecomesCurrent()
        {
            _rootA = new GameObject(nameof(UIRootBinding));
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();

            Assert.AreSame(_a, UIRootBinding.TryGetInstance(), "登记后应立即成为当前 UI 根");
        }

        [Test]
        public void Destroy_Current_ClearsCurrent()
        {
            _rootA = new GameObject(nameof(UIRootBinding));
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();
            Assert.IsNotNull(UIRootBinding.TryGetInstance(), "前置条件：已登记");

            UnityEngine.Object.DestroyImmediate(_rootA);
            _rootA = null;
            _a = null;

            Assert.IsNull(UIRootBinding.TryGetInstance(), "当前根销毁后不应留下悬空引用");
        }

        [Test]
        public void Bind_Duplicate_FirstWinsAndLaterIsRejected()
        {
            _rootA = new GameObject("UIRootA");
            _a = _rootA.AddComponent<UIRootBinding>();
            _a.Internal_Bind();

            _rootB = new GameObject("UIRootB");
            _b = _rootB.AddComponent<UIRootBinding>();
            // 基类先到先得：后到者整物体 Destroy（EditMode 下 Destroy 只报错不落账，仍以首任为准）
            UtfLogExpect.Error();
            _b.Internal_Bind();

            Assert.AreSame(_a, UIRootBinding.TryGetInstance(), "先到先得：后到者不得抢位");
            Assert.AreNotSame(_a, _b, "两次登记应是不同实例");
        }

        [Test]
        public void TryGetInstance_DoesNotAutoCreate()
        {
            Assert.IsNull(UIRootBinding.TryGetInstance(), "场景里没有 UIRootBinding 时取值必须是 null，不得自动创建空物体");
        }
    }
}
