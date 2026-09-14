using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;

namespace Service.Input
{
    /// <summary>
    /// 虚拟输入注册表（<see cref="UIMobileInputRegistry"/>）行为测试：
    /// 自注册语义、空名跳过、重名覆盖、实例判等注销、销毁惰性清除。
    /// <para>Edit Mode 下普通 MonoBehaviour 不会自动 OnEnable，测试经 internal Register/Unregister 直驱。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIMobileInputRegistryTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ClearRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _objects.Clear();
            ClearRegistry();
        }

        private static void ClearRegistry()
        {
            typeof(UIMobileInputRegistry)
                .GetMethod("ResetStaticsForDomainReloadDisabled", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null);
        }

        private GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
        }

        private static void SetActionName(Component component, string actionName)
        {
            var field = component.GetType().GetField("m_ActionName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "InputButton/InputAxes 应存在 m_ActionName 序列化字段");
            field.SetValue(component, actionName);
        }

        private InputButton CreateButton(string actionName)
        {
            var go = Track(new GameObject("btn-" + actionName));
            var button = go.AddComponent<InputButton>();
            SetActionName(button, actionName);
            return button;
        }

        private InputAxes CreateAxes(string actionName)
        {
            var go = Track(new GameObject("axes-" + actionName));
            var axes = go.AddComponent<InputAxes>();
            SetActionName(axes, actionName);
            return axes;
        }

        [Test]
        public void Register_ThenTryGet_ReturnsSameInstance()
        {
            var button = CreateButton("Fire");
            UIMobileInputRegistry.Register(button);

            Assert.IsTrue(UIMobileInputRegistry.TryGetButton("Fire", out var found));
            Assert.AreSame(button, found);
        }

        [Test]
        public void Register_EmptyActionName_IsSkipped()
        {
            var button = CreateButton("");

            UIMobileInputRegistry.Register(button);

            Assert.IsFalse(UIMobileInputRegistry.TryGetButton("", out _));
            Assert.IsFalse(UIMobileInputRegistry.TryGetButton(button.ActionName, out _));
        }

        [Test]
        public void Register_DuplicateName_OverridesAndKeepsLatest()
        {
            var first = CreateButton("Fire");
            var second = CreateButton("Fire");
            UIMobileInputRegistry.Register(first);
            UIMobileInputRegistry.Register(second);

            Assert.IsTrue(UIMobileInputRegistry.TryGetButton("Fire", out var found));
            Assert.AreSame(second, found);
        }

        [Test]
        public void Unregister_OtherInstance_DoesNotRemoveCurrentMapping()
        {
            var first = CreateButton("Fire");
            var second = CreateButton("Fire");
            UIMobileInputRegistry.Register(first);
            UIMobileInputRegistry.Register(second);

            UIMobileInputRegistry.Unregister(first);

            Assert.IsTrue(UIMobileInputRegistry.TryGetButton("Fire", out var found));
            Assert.AreSame(second, found, "旧组件注销不得误删后注册的同名组件");
        }

        [Test]
        public void Unregister_Self_RemovesMapping()
        {
            var button = CreateButton("Fire");
            UIMobileInputRegistry.Register(button);

            UIMobileInputRegistry.Unregister(button);

            Assert.IsFalse(UIMobileInputRegistry.TryGetButton("Fire", out _));
        }

        [Test]
        public void TryGet_DestroyedButton_IsLazilyRemoved()
        {
            var button = CreateButton("Fire");
            UIMobileInputRegistry.Register(button);
            var go = button.gameObject;

            Object.DestroyImmediate(go);
            // DestroyImmediate 后组件为 null，但字典可能仍持有条目（Edit Mode 不保证 OnDisable）
            Assert.IsFalse(UIMobileInputRegistry.TryGetButton("Fire", out _),
                "已销毁组件应惰性清除并返回 false");
        }

        [Test]
        public void Axes_RegisterAndQuery_Works()
        {
            var axes = CreateAxes("Move");
            UIMobileInputRegistry.Register(axes);

            Assert.IsTrue(UIMobileInputRegistry.TryGetAxes("Move", out var found));
            Assert.AreSame(axes, found);

            UIMobileInputRegistry.Unregister(axes);
            Assert.IsFalse(UIMobileInputRegistry.TryGetAxes("Move", out _));
        }

        [Test]
        public void CombinedGroupAndFlatName_AreDistinctKeys()
        {
            var combined = CreateButton("Player/Fire");
            var flat = CreateButton("Fire");
            UIMobileInputRegistry.Register(combined);
            UIMobileInputRegistry.Register(flat);

            Assert.IsTrue(UIMobileInputRegistry.TryGetButton("Player/Fire", out var a));
            Assert.IsTrue(UIMobileInputRegistry.TryGetButton("Fire", out var b));
            Assert.AreSame(combined, a);
            Assert.AreSame(flat, b);
        }
    }
}
