using Moirai.Atropos.Input;
using NUnit.Framework;

namespace Service.Input
{
    /// <summary>
    /// 焦点与输入 Enabled 联动守卫（<see cref="FocusInputGuard"/>）单元测试：
    /// 失焦记录/回焦还原、重复焦点事件去重（防止连续失焦后输入永久关闭）。
    /// </summary>
    [TestFixture]
    public sealed class FocusInputGuardTests
    {
        private FocusInputGuard _guard;

        [SetUp]
        public void SetUp()
        {
            _guard = new FocusInputGuard();
        }

        [Test]
        public void InitialFocusGain_IsIgnored()
        {
            // 已处于有焦点态，重复的 ApplicationFocus 不应改写 Enabled
            Assert.IsNull(_guard.Evaluate(true, currentEnabled: false));
        }

        [Test]
        public void FocusLoss_DisablesAndRemembersPreviousEnabled()
        {
            bool? target = _guard.Evaluate(hasFocus: false, currentEnabled: true);

            Assert.IsFalse(target);
        }

        [Test]
        public void FocusGain_RestoresEnabledFromBeforeLoss()
        {
            _guard.Evaluate(hasFocus: false, currentEnabled: true);

            bool? target = _guard.Evaluate(hasFocus: true, currentEnabled: false);

            Assert.IsTrue(target);
        }

        [Test]
        public void FocusGain_RestoresDisabled_WhenBusinessDisabledBeforeLoss()
        {
            _guard.Evaluate(hasFocus: false, currentEnabled: false);

            bool? target = _guard.Evaluate(hasFocus: true, currentEnabled: false);

            Assert.IsFalse(target);
        }

        [Test]
        public void DoubleFocusLoss_DoesNotOverwriteRememberedEnabled()
        {
            // 关键回归：连续两次失焦时，第二次不得把记录值覆盖为 false
            _guard.Evaluate(hasFocus: false, currentEnabled: true);
            bool? second = _guard.Evaluate(hasFocus: false, currentEnabled: false);

            Assert.IsNull(second, "重复失焦事件应被忽略");

            bool? restore = _guard.Evaluate(hasFocus: true, currentEnabled: false);
            Assert.IsTrue(restore, "回焦应还原为失焦前的 true，而不是被第二次失焦污染的 false");
        }

        [Test]
        public void DoubleFocusGain_IsIgnored()
        {
            _guard.Evaluate(hasFocus: false, currentEnabled: true);
            _guard.Evaluate(hasFocus: true, currentEnabled: false);

            // 已回焦，再来一次 ApplicationFocus 不应改写
            Assert.IsNull(_guard.Evaluate(hasFocus: true, currentEnabled: false));
        }

        [Test]
        public void Reset_RestoresInitialFocusState()
        {
            _guard.Evaluate(hasFocus: false, currentEnabled: false);
            _guard.Reset();

            // 重置后视为有焦点：重复 gain 忽略；随后 loss 应重新记录
            Assert.IsNull(_guard.Evaluate(hasFocus: true, currentEnabled: true));
            Assert.IsFalse(_guard.Evaluate(hasFocus: false, currentEnabled: true));
        }
    }
}
