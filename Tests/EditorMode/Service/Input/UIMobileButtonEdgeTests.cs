using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;

namespace Service.Input
{
    /// <summary>
    /// 移动端虚拟按钮边沿与 Handler 查询契约测试。
    /// <para>边沿在 <see cref="InputButton"/> 指针事件里按帧闩锁：同帧点按不丢边、
    /// 未查询帧不产生假边沿、Reset 清掉残留状态。Edit Mode 帧号不推进，跨帧语义经
    /// <c>WasPressedAt</c>/<c>WasReleasedAt</c> 注入帧号验证；Handler 路径使用当前
    /// <c>Time.frameCount</c> 验证同帧幂等。</para>
    /// </summary>
    [TestFixture]
    public sealed class UIMobileButtonEdgeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private UIMobileInputHandler _handler;

        [SetUp]
        public void SetUp()
        {
            ClearRegistry();
            _handler = new UIMobileInputHandler();
            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
            _handler = null;

            foreach (var go in _objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _objects.Clear();
            ClearRegistry();
        }

        private static void ClearRegistry() => UIMobileInputRegistry.ResetStaticsForDomainReloadDisabled();

        private InputButton CreateRegisteredButton(string actionName)
        {
            var go = new GameObject("btn-" + actionName);
            _objects.Add(go);
            var button = go.AddComponent<InputButton>();
            var field = typeof(InputButton).GetField("m_ActionName", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(button, actionName);
            UIMobileInputRegistry.Register(button);
            return button;
        }

        #region 组件边沿闩锁 [COMPONENT EDGE]

        [Test]
        public void Press_LatchesEdgeOnlyOnThatFrame()
        {
            var go = new GameObject("edge");
            _objects.Add(go);
            var button = go.AddComponent<InputButton>();

            button.Press(10);

            Assert.IsTrue(button.WasPressedAt(10));
            Assert.IsFalse(button.WasPressedAt(11));
            Assert.IsTrue(button.BoolValue);
        }

        [Test]
        public void SameFrameTap_PressesAndReleases_BothEdgesTrue()
        {
            var go = new GameObject("tap");
            _objects.Add(go);
            var button = go.AddComponent<InputButton>();

            button.Press(5);
            button.Release(5);

            Assert.IsTrue(button.WasPressedAt(5), "同帧点按不得丢失按下边沿");
            Assert.IsTrue(button.WasReleasedAt(5), "同帧点按不得丢失抬起边沿");
            Assert.IsFalse(button.BoolValue);
        }

        [Test]
        public void PressWithoutPriorQuery_NextFrameDoesNotFabricateDownEdge()
        {
            // 回归：旧懒采样会在「按下帧未查询」时于下一帧补出 Entered
            var go = new GameObject("late");
            _objects.Add(go);
            var button = go.AddComponent<InputButton>();

            button.Press(7);
            // 帧 7 无人查询 GetButtonDown；帧 8 仍按住
            Assert.IsFalse(button.WasPressedAt(8));
            Assert.IsTrue(button.BoolValue);
        }

        [Test]
        public void ResetState_ClearsBoolAndEdges()
        {
            var go = new GameObject("reset");
            _objects.Add(go);
            var button = go.AddComponent<InputButton>();

            button.Press(3);
            button.ResetState();

            Assert.IsFalse(button.BoolValue);
            Assert.IsFalse(button.WasPressedAt(3));
            Assert.IsFalse(button.WasReleasedAt(3));
        }

        #endregion

        #region Handler 查询契约 [HANDLER CONTRACT]

        [Test]
        public void Handler_GetButtonDown_IsIdempotentWithinSameFrame()
        {
            var button = CreateRegisteredButton("Jump");
            button.Press(Time.frameCount);

            Assert.IsTrue(_handler.GetButtonDown("Jump"));
            Assert.IsTrue(_handler.GetButtonDown("Jump"));
            Assert.IsTrue(_handler.GetBool("Jump"));
        }

        [Test]
        public void Handler_SameFrameTap_ReportsDownAndUp()
        {
            var button = CreateRegisteredButton("Jump");
            int frame = Time.frameCount;
            button.Press(frame);
            button.Release(frame);

            Assert.IsTrue(_handler.GetButtonDown("Jump"));
            Assert.IsTrue(_handler.GetButtonUp("Jump"));
            Assert.IsFalse(_handler.GetBool("Jump"));
        }

        [Test]
        public void Handler_PrefersGroupQualifiedThenFallsBackToFlat()
        {
            CreateRegisteredButton("Player/Fire");
            CreateRegisteredButton("Fire");

            int frame = Time.frameCount;
            UIMobileInputRegistry.TryGetButton("Player/Fire", out var combined);
            UIMobileInputRegistry.TryGetButton("Fire", out var flat);
            combined.Press(frame);

            Assert.IsTrue(_handler.GetButtonDown("Fire", "Player"), "应命中 Group/Name");
            Assert.IsFalse(_handler.GetButtonDown("Fire"), "平铺名是另一组件，不应串边沿");

            flat.Press(frame);
            Assert.IsTrue(_handler.GetButtonDown("Fire"));
        }

        [Test]
        public void Handler_ResetAllInputStates_ClearsButtonsAndAxes()
        {
            var button = CreateRegisteredButton("Jump");
            var axesGo = new GameObject("axes");
            _objects.Add(axesGo);
            var axes = axesGo.AddComponent<InputAxes>();
            typeof(InputAxes).GetField("m_ActionName", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(axes, "Move");
            UIMobileInputRegistry.Register(axes);

            button.Press(Time.frameCount);
            axes.Vector2Value = new Vector2(0.5f, -0.25f);

            _handler.ResetAllInputStates();

            Assert.IsFalse(_handler.GetBool("Jump"));
            Assert.IsFalse(_handler.GetButtonDown("Jump"));
            Assert.AreEqual(Vector2.zero, _handler.GetVector2("Move"));
        }

        [Test]
        public void Handler_EnterUIModal_ResetsHeldVirtualInput()
        {
            var button = CreateRegisteredButton("Jump");
            button.Press(Time.frameCount);

            Assert.IsTrue(_handler.GetBool("Jump"));

            _handler.SetUIModal(true);

            Assert.IsFalse(_handler.GetBool("Jump"), "进入模态应清掉残留按住状态");
            Assert.IsFalse(_handler.GetButtonDown("Jump"));
        }

        [Test]
        public void Handler_MissingAction_ReturnsDefaults()
        {
            Assert.IsFalse(_handler.GetButtonDown("Nope"));
            Assert.IsFalse(_handler.GetButtonUp("Nope"));
            Assert.IsFalse(_handler.GetBool("Nope"));
            Assert.AreEqual(Vector2.zero, _handler.GetVector2("Nope"));
            Assert.AreEqual(0f, _handler.GetFloat("Nope"));
        }

        #endregion
    }
}
