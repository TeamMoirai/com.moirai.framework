using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// <see cref="AudioMixStateMachine"/> 的优先级与回落契约。
    /// </summary>
    /// <remarks>自动 Ducking 寄生在这套规则上（Voice 起播借走 Dialogue、播完归还借走的那一层），故用例锁的是「谁能打断谁」与「回落会不会越权」，而非 Ducking 开关本身。</remarks>
    [TestFixture]
    public class AudioMixStateMachineTests
    {
        private AudioMixStateMachine _machine;
        private readonly List<EMixSnapshot> _applied = new List<EMixSnapshot>();
        private readonly List<string> _capturedMessages = new List<string>();

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

            _capturedMessages.Clear();
            AudioWarnOnce.Reset();
            LogUtility.OnMessageLogged += OnMessageLogged;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= OnMessageLogged;
            AudioWarnOnce.Reset();
        }

        private void OnMessageLogged(ELogLevel level, string message, Exception exception)
            => _capturedMessages.Add(message);

        private bool Mentions(string fragment)
            => _capturedMessages.Exists(m => m != null && m.Contains(fragment, StringComparison.Ordinal));

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

        #region 自动绑定 [AUTO BIND]

        // 名字匹配已交回 Unity 公开的 AudioMixer.FindSnapshot（本地既无 Snapshot 枚举接口，
        // 也没有可注入的假 Mixer）；告警口径用例直接打 WarnUnboundAfterAutoBind 接缝。

        [Test]
        public void TryBindSnapshotsByName_NullMixer_IsNoOp()
        {
            int bound = _machine.TryBindSnapshotsByName(null);
            Assert.AreEqual(0, bound);
        }

        [Test]
        public void FindMixerSnapshot_NullMixer_ReturnsNull()
        {
            Assert.IsNull(AudioMixStateMachine.FindMixerSnapshot(null, EMixSnapshot.Dialogue));
        }

        [Test]
        public void WarnUnboundAfterAutoBind_NoConfig_DoesNotWarn()
        {
            _machine.WarnUnboundAfterAutoBind(null);
            _machine.WarnUnboundAfterAutoBind(Array.Empty<AudioMixSnapshotEntry>());

            Assert.IsEmpty(_capturedMessages, "没配置走默认，不得报 WarnUnboundAfterAutoBind");
        }

        [Test]
        public void WarnUnboundAfterAutoBind_ConfiguredUnbound_Warns()
        {
            var configured = new[]
            {
                new AudioMixSnapshotEntry { State = EMixSnapshot.Dialogue, Snapshot = null },
            };

            UtfLogExpect.Warning();
            _machine.WarnUnboundAfterAutoBind(configured);

            Assert.IsTrue(Mentions("已配置状态 Dialogue"), "已配置且仍无 Snapshot 必须告警");
            Assert.IsFalse(Mentions("Cinematic"), "未配置状态不得被顺带告警");
        }

        [Test]
        public void WarnUnboundAfterAutoBind_UnconfiguredState_DoesNotWarn()
        {
            // 只登记 Dialogue；Cinematic 未配置，即便状态机里同样绑不上也不告警
            var configured = new[]
            {
                new AudioMixSnapshotEntry { State = EMixSnapshot.Dialogue, Snapshot = null },
            };

            UtfLogExpect.Warning();
            _machine.WarnUnboundAfterAutoBind(configured);

            Assert.IsTrue(Mentions("Dialogue"));
            Assert.IsFalse(Mentions("Cinematic"));
            Assert.IsFalse(Mentions("Paused"));
            Assert.IsFalse(Mentions("Muffled"));
            Assert.IsFalse(Mentions("LowHealth"));
        }

        [Test]
        public void WarnUnboundAfterAutoBind_DefaultUnbound_DoesNotWarn()
        {
            var configured = new[]
            {
                new AudioMixSnapshotEntry { State = EMixSnapshot.Default, Snapshot = null },
            };

            _machine.WarnUnboundAfterAutoBind(configured);

            Assert.IsEmpty(_capturedMessages, "Default 回 Mixer 默认态属正常，例外不告警");
        }

        #endregion 自动绑定 [AUTO BIND]
    }
}
