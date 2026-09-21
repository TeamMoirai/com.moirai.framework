using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// <see cref="AudioMixStateMachine"/> 的优先级与回落契约。
    /// <para>自动 Ducking 完全寄生在这套规则上（Voice 起播借走 Dialogue、播完归还借走的那一层），
    /// 所以这里锁的是「谁能打断谁」与「回落会不会越权」，而不是 Ducking 的开关本身。</para>
    /// </summary>
    [TestFixture]
    public class AudioMixStateMachineTests
    {
        private AudioMixStateMachine _machine;

        [SetUp]
        public void SetUp()
        {
            _machine = new AudioMixStateMachine();
            // mixer 传 null 只为铺出各状态的内置默认优先级；无快照时切换仅推进状态
            _machine.BindFromMixer(null);
        }

        private bool Request(EMixSnapshot target, bool force = false) => _machine.Request(target, 0.1f, force);

        [Test]
        public void InitialState_IsDefault()
        {
            Assert.AreEqual(EMixSnapshot.Default, _machine.Current);
        }

        [Test]
        public void Request_SameState_IsNoOp()
        {
            Assert.IsFalse(Request(EMixSnapshot.Default), "重复请求当前状态不该触发过渡");
        }

        [Test]
        public void Request_HigherPriorityCanTakeOver()
        {
            Assert.IsTrue(Request(EMixSnapshot.Dialogue));
            Assert.AreEqual(EMixSnapshot.Dialogue, _machine.Current);

            Assert.IsTrue(Request(EMixSnapshot.Cinematic), "Cinematic 优先级更高，应接走混音");
            Assert.AreEqual(EMixSnapshot.Cinematic, _machine.Current);
        }

        [Test]
        public void Request_LowerPriorityIsRefused()
        {
            _machine.Request(EMixSnapshot.Cinematic, 0.1f);

            // Ducking 的让步路径：对白请求不得盖过演出
            Assert.IsFalse(Request(EMixSnapshot.Dialogue));
            Assert.IsFalse(Request(EMixSnapshot.Muffled));
            Assert.AreEqual(EMixSnapshot.Cinematic, _machine.Current);
        }

        [Test]
        public void Request_ForceBreaksPriorityGuard()
        {
            _machine.Request(EMixSnapshot.Cinematic, 0.1f);

            Assert.IsTrue(Request(EMixSnapshot.Dialogue, force: true));
            Assert.AreEqual(EMixSnapshot.Dialogue, _machine.Current);
        }

        [Test]
        public void Request_DowngradeNeedsForce_DuckingRestoreUsesIt()
        {
            Assert.IsTrue(Request(EMixSnapshot.Dialogue));

            // 回落必然是降优先级（Default 为 0）：非 force 走不动，Ducking 的归还因此必须带 force
            Assert.IsFalse(Request(EMixSnapshot.Default));
            Assert.AreEqual(EMixSnapshot.Dialogue, _machine.Current);

            Assert.IsTrue(Request(EMixSnapshot.Default, force: true));
            Assert.AreEqual(EMixSnapshot.Default, _machine.Current);
        }

        [Test]
        public void ResetToDefault_WinsFromAnyState()
        {
            _machine.Request(EMixSnapshot.Cinematic, 0.1f);
            _machine.ResetToDefault(0.1f);

            Assert.AreEqual(EMixSnapshot.Default, _machine.Current);
        }

        [Test]
        public void SetSnapshot_CanRaisePriorityAboveBuiltin()
        {
            _machine.SetSnapshot(EMixSnapshot.Dialogue, null, 5f);
            _machine.Request(EMixSnapshot.Cinematic, 0.1f);

            Assert.IsTrue(Request(EMixSnapshot.Dialogue), "被抬升优先级的状态应能打断 Cinematic");
        }

        [Test]
        public void MiddlewareTransitionHandler_ReplacesMixerPath()
        {
            EMixSnapshot seen = EMixSnapshot.Default;
            float blend = -1f;
            _machine.SetMiddlewareTransitionHandler((state, seconds) =>
            {
                seen = state;
                blend = seconds;
            });

            Assert.IsTrue(Request(EMixSnapshot.Dialogue));

            Assert.AreEqual(EMixSnapshot.Dialogue, seen, "中间件回调应收到目标状态");
            Assert.AreEqual(0.1f, blend, 0.0001f, "交叉淡变时长应透传给中间件");
        }

        [Test]
        public void StateAdvancesEvenWithoutSnapshot_WhenMixerMissing()
        {
            // 无 Mixer 无快照时切换是空操作，但状态记账仍推进：上层据此判断「是否还轮到自己」
            Assert.IsTrue(Request(EMixSnapshot.Muffled));
            Assert.AreEqual(EMixSnapshot.Muffled, _machine.Current);
        }
    }
}
