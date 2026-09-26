using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;

namespace Service.Input
{
    /// <summary>
    /// <see cref="PreventInputOnEnable"/> 所有权语义测试：
    /// OnDisable 仅恢复本组件实际修改过的标志，未勾选选项不得触碰全局状态。
    /// <para>Edit Mode 下普通 MonoBehaviour 不自动调用 OnEnable/OnDisable，经反射直驱（生命周期唤起，反射白名单第 2 类）。
    /// 勾选字段是 internal，直接赋值，不再反射。</para>
    /// <para>经 <c>InputService.Handler</c> 注入真实 UIMobile 后端（InternalsVisibleTo + 生成的 setter）。</para>
    /// </summary>
    [TestFixture]
    public sealed class PreventInputOnEnableTests
    {
        private static readonly BindingFlags InstancePrivate =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _objects = new List<GameObject>();
        private UIMobileInputHandler _handler;
        private InputServiceHandler _savedHandler;

        [SetUp]
        public void SetUp()
        {
            _savedHandler = InputService.Internal_PeekHandler();
            _handler = new UIMobileInputHandler();
            InputService.Handler = _handler;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _objects.Clear();

            if (_handler != null)
            {
                _handler.Internal_Shutdown();
                _handler = null;
            }

            InputService.Internal_UseHandler(_savedHandler);
        }

        private PreventInputOnEnable CreateComponent(bool lockPlayer, bool preventUi)
        {
            var go = new GameObject("prevent-input");
            _objects.Add(go);
            var component = go.AddComponent<PreventInputOnEnable>();
            component.m_LockPlayerController = lockPlayer;
            component.m_PreventInteractionUI = preventUi;
            return component;
        }

        private static void Invoke(PreventInputOnEnable component, string method)
        {
            component.GetType().GetMethod(method, InstancePrivate)
                .Invoke(component, null);
        }

        [Test]
        public void OnlyLockEnabled_OnDisable_DoesNotTouchPreventUI()
        {
            // 业务侧主动打开了 PreventInteractionUI，本组件只勾了 Lock
            InputService.PreventInteractionUI = true;
            Assert.IsFalse(InputService.LockPlayerController);

            var component = CreateComponent(lockPlayer: true, preventUi: false);
            Invoke(component, "OnEnable");

            Assert.IsTrue(InputService.LockPlayerController);
            Assert.IsTrue(InputService.PreventInteractionUI);

            Invoke(component, "OnDisable");

            Assert.IsFalse(InputService.LockPlayerController, "自己改过的 Lock 应还原");
            Assert.IsTrue(InputService.PreventInteractionUI, "未勾选的 PreventUI 不得被误清");
        }

        [Test]
        public void OnlyPreventUIEnabled_OnDisable_DoesNotTouchLock()
        {
            InputService.LockPlayerController = true;

            var component = CreateComponent(lockPlayer: false, preventUi: true);
            Invoke(component, "OnEnable");

            Assert.IsTrue(InputService.LockPlayerController);
            Assert.IsTrue(InputService.PreventInteractionUI);

            Invoke(component, "OnDisable");

            Assert.IsTrue(InputService.LockPlayerController, "未勾选的 Lock 不得被误清");
            Assert.IsFalse(InputService.PreventInteractionUI);
        }

        [Test]
        public void BothEnabled_RestoresBothPreviousValues()
        {
            var component = CreateComponent(lockPlayer: true, preventUi: true);
            Invoke(component, "OnEnable");

            Assert.IsTrue(InputService.LockPlayerController);
            Assert.IsTrue(InputService.PreventInteractionUI);

            Invoke(component, "OnDisable");

            Assert.IsFalse(InputService.LockPlayerController);
            Assert.IsFalse(InputService.PreventInteractionUI);
        }

        [Test]
        public void DisableWithoutEnable_IsNoOp()
        {
            InputService.LockPlayerController = true;
            InputService.PreventInteractionUI = true;

            var component = CreateComponent(lockPlayer: true, preventUi: true);
            Invoke(component, "OnDisable");

            Assert.IsTrue(InputService.LockPlayerController);
            Assert.IsTrue(InputService.PreventInteractionUI);
        }

        [Test]
        public void EnableDisableTwice_RestoresConsistently()
        {
            var component = CreateComponent(lockPlayer: true, preventUi: false);

            Invoke(component, "OnEnable");
            Invoke(component, "OnDisable");
            Assert.IsFalse(InputService.LockPlayerController);

            Invoke(component, "OnEnable");
            Assert.IsTrue(InputService.LockPlayerController);
            Invoke(component, "OnDisable");
            Assert.IsFalse(InputService.LockPlayerController);
        }
    }
}
