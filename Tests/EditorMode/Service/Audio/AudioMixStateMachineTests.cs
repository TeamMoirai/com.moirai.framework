using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine.TestTools;

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
        private readonly System.Collections.Generic.List<EMixSnapshot> _applied =
            new System.Collections.Generic.List<EMixSnapshot>();

        [SetUp]
        public void SetUp()
        {
            _machine = new AudioMixStateMachine();
            // 铺出各状态的内置默认优先级；mixer 传 null 意味着"无 Mixer 可施加"
            _machine.BindFromMixer(null);

            // Request 只有在真的能施加时才返回 true（无 Mixer 又无施加通道一律拒绝），
            // 所以优先级/回落类用例必须挂上施加通道——这里复用生产就有的中间件回调接缝。
            _applied.Clear();
            _machine.SetMiddlewareTransitionHandler((state, _) => _applied.Add(state));
        }

        private bool Request(EMixSnapshot target, bool force = false) => _machine.Request(target, 0.1f, force);

        [Test]
        public void Request_IsRefused_WhenNothingCanApply()
        {
            // 无 Mixer、无施加通道：既不该改状态，也不该让调用方以为"混音是我借走的"
            var bare = new AudioMixStateMachine();
            bare.BindFromMixer(null);

            LogAssert.ignoreFailingMessages = true;
            bool accepted;
            try
            {
                accepted = bare.Request(EMixSnapshot.Dialogue, 0.1f);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsFalse(accepted, "什么都施加不了时不得算成功");
            Assert.AreEqual(EMixSnapshot.Default, bare.Current, "未施加的切换不该推进状态记账");
        }

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
            Assert.AreEqual(1, _applied.Count, "被优先级挡下的请求不得施加到混音上");
            Assert.AreEqual(EMixSnapshot.Cinematic, _applied[0]);
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

        #region 自动绑定命名 [AUTO BIND NAMING]

        [Test]
        public void ResolveSnapshotIndex_ExactOrdinalName_Matches()
        {
            var names = new[] { "Default", "Paused", "Dialogue", "Cinematic", "Muffled", "LowHealth" };

            Assert.AreEqual(0, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Default));
            Assert.AreEqual(2, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Dialogue));
            Assert.AreEqual(5, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.LowHealth));
        }

        [Test]
        public void ResolveSnapshotIndex_IgnoreCase_FallbackMatches()
        {
            var names = new[] { "default", "PAUSED", "dialogue" };

            Assert.AreEqual(0, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Default));
            Assert.AreEqual(1, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Paused));
            Assert.AreEqual(2, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Dialogue));
        }

        [Test]
        public void ResolveSnapshotIndex_ExactBeatsIgnoreCase()
        {
            var names = new[] { "DIALOGUE", "Dialogue" };

            Assert.AreEqual(1, AudioMixStateMachine.ResolveSnapshotIndex(names, EMixSnapshot.Dialogue),
                "精确序数命中应优先于忽略大小写命中");
        }

        [Test]
        public void ResolveSnapshotIndex_MissingOrNullNames_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, AudioMixStateMachine.ResolveSnapshotIndex(null, EMixSnapshot.Paused));
            Assert.AreEqual(-1, AudioMixStateMachine.ResolveSnapshotIndex(System.Array.Empty<string>(), EMixSnapshot.Paused));
            Assert.AreEqual(-1, AudioMixStateMachine.ResolveSnapshotIndex(new[] { "Other", null, "" }, EMixSnapshot.Paused));
        }

        [Test]
        public void TryBindSnapshotsByName_NullMixer_IsNoOp()
        {
            int bound = _machine.TryBindSnapshotsByName(null);
            Assert.AreEqual(0, bound);
        }

        [Test]
        public void CollectMixerSnapshots_NullMixer_ReturnsEmpty()
        {
            Assert.IsEmpty(AudioMixStateMachine.CollectMixerSnapshots(null));
        }

        #endregion 自动绑定命名 [AUTO BIND NAMING]
    }
}
