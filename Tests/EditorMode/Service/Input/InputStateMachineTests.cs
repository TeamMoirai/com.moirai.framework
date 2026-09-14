using Moirai.Atropos.Input;
using NUnit.Framework;

namespace Service.Input
{
    /// <summary>
    /// 输入状态机（<see cref="InputStateMachine"/>）组合语义单元测试：
    /// Enabled/LockPlayerController/PreventInteractionUI/UIModal 四个压制态的
    /// 边沿触发（幂等赋值不重复触发）、强制派生语义（未启用/模态时 Lock 强制为 true）、
    /// 以及进入压制态时 <c>ResetRequested</c> 的副作用收敛。
    /// <para>纯逻辑测试，不依赖 Unity 场景与输入后端。</para>
    /// </summary>
    [TestFixture]
    public sealed class InputStateMachineTests
    {
        private InputStateMachine _state;
        private int _resetCount;

        [SetUp]
        public void SetUp()
        {
            _state = new InputStateMachine();
            _resetCount = 0;
            _state.ResetRequested += () => _resetCount++;
        }

        #region Enabled [启用]

        [Test]
        public void Enabled_DefaultsToTrue()
        {
            Assert.IsTrue(_state.Enabled);
            Assert.AreEqual(0, _resetCount);
        }

        [Test]
        public void Enabled_SetFalse_FiresResetOnce()
        {
            _state.Enabled = false;

            Assert.IsFalse(_state.Enabled);
            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void Enabled_IdempotentSet_DoesNotFireReset()
        {
            _state.Enabled = false;
            _state.Enabled = false;

            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void Enabled_RestoreTrue_DoesNotFireReset()
        {
            _state.Enabled = false;
            _resetCount = 0;

            _state.Enabled = true;

            Assert.AreEqual(0, _resetCount);
        }

        #endregion

        #region LockPlayerController [锁定玩家控制器]

        [Test]
        public void Lock_SetTrue_FiresResetOnce()
        {
            _state.LockPlayerController = true;

            Assert.IsTrue(_state.LockPlayerController);
            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void Lock_SetFalse_DoesNotFireReset()
        {
            _state.LockPlayerController = true;
            _resetCount = 0;

            _state.LockPlayerController = false;

            Assert.IsFalse(_state.LockPlayerController);
            Assert.AreEqual(0, _resetCount);
        }

        [Test]
        public void Lock_ForcedTrue_WhenDisabled()
        {
            _state.Enabled = false;

            // 未启用时读取强制为 true（压制语义），且 setter 未被触碰
            Assert.IsTrue(_state.LockPlayerController);
        }

        [Test]
        public void Lock_ForcedTrue_WhileUIModal()
        {
            _state.SetUIModal(true);

            Assert.IsTrue(_state.LockPlayerController);
        }

        #endregion

        #region PreventInteractionUI [禁止 UI 交互]

        [Test]
        public void PreventUI_SetTrue_FiresResetOnce()
        {
            _state.PreventInteractionUI = true;

            Assert.IsTrue(_state.PreventInteractionUI);
            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void PreventUI_ForcedTrue_WhenDisabled()
        {
            _state.Enabled = false;

            Assert.IsTrue(_state.PreventInteractionUI);
        }

        #endregion

        #region UIModal [UI 模态]

        [Test]
        public void UIModal_Enter_FiresResetOnce()
        {
            _state.SetUIModal(true);

            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void UIModal_Exit_DoesNotFireReset()
        {
            // 退出模态不重置——玩家可能正合法按住输入
            _state.SetUIModal(true);
            _resetCount = 0;

            _state.SetUIModal(false);

            Assert.AreEqual(0, _resetCount);
        }

        [Test]
        public void UIModal_IdempotentEnter_DoesNotFireResetAgain()
        {
            _state.SetUIModal(true);
            _state.SetUIModal(true);

            Assert.AreEqual(1, _resetCount);
        }

        [Test]
        public void UIModal_IdempotentExit_DoesNotFireReset()
        {
            _state.SetUIModal(false);
            _state.SetUIModal(false);

            Assert.AreEqual(0, _resetCount);
        }

        #endregion

        #region 组合语义 [COMPOSITION]

        [Test]
        public void Composition_DisableThenLockSetterUnchanged_RestoreKeepsUnlocked()
        {
            // 未启用时 Lock 读取被强制为 true，但不改写底层 setter 状态——
            // 重新启用后应回到未锁定，而不是被"传染"为锁定
            _state.Enabled = false;
            Assert.IsTrue(_state.LockPlayerController);

            _state.Enabled = true;

            Assert.IsFalse(_state.LockPlayerController);
        }

        [Test]
        public void Composition_MultipleSuppressStates_FiresResetPerTransition()
        {
            _state.LockPlayerController = true;
            _state.PreventInteractionUI = true;
            _state.Enabled = false;

            Assert.AreEqual(3, _resetCount);
        }

        #endregion
    }
}
