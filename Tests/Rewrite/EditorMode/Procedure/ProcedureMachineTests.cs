using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Procedure.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Procedure
{
    public sealed class ProcedureMachineTests
    {
        private sealed class Probe : IProcedure
        {
            internal Probe(string key) { Key = key; }
            public string Key { get; }
            internal readonly List<string> Calls = new List<string>();
            internal UniTaskCompletionSource<bool> EnterGate;
            internal float LastElapsed;
            internal float LastReal;
            public async UniTask OnEnterAsync(CancellationToken cancellationToken)
            {
                Calls.Add("enter");
                if (EnterGate != null) await EnterGate.Task;
            }
            public void OnUpdate(float elapsedSeconds, float realElapsedSeconds)
            {
                Calls.Add("update");
                LastElapsed = elapsedSeconds;
                LastReal = realElapsedSeconds;
            }
            public UniTask OnLeaveAsync(bool isShutdown, CancellationToken cancellationToken)
            {
                Calls.Add(isShutdown ? "shutdown" : "leave");
                return UniTask.CompletedTask;
            }
        }

        private sealed class Clock : IClock
        {
            public float Time { get; set; }
            public float DeltaTime { get; set; }
            public double RealtimeSinceStartup { get; set; }
        }

        [UnityTest]
        public IEnumerator SameKey_Restarts_AndUnknownKeyDoesNotLeave() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe("a");
            var machine = new ProcedureMachine(new Dictionary<string, IProcedure> { { "a", probe } });
            try
            {
                await machine.SwitchToAsync("a", CancellationToken.None);
                await machine.SwitchToAsync("a", CancellationToken.None);
                var failure = Assert.Throws<InvalidOperationException>(() =>
                    machine.SwitchToAsync("missing", CancellationToken.None).GetAwaiter().GetResult());
                StringAssert.Contains("missing", failure.Message);
                CollectionAssert.AreEqual(new[] { "enter", "leave", "enter" }, probe.Calls);
                machine.Update(0.25f, 0.5f);
                Assert.AreEqual(0.25f, probe.LastElapsed);
                Assert.AreEqual(0.5f, probe.LastReal);
            }
            finally { await machine.StopAsync(CancellationToken.None); }
            await machine.StopAsync(CancellationToken.None);
            Assert.AreEqual(1, probe.Calls.FindAll(x => x == "shutdown").Count);
        });

        [UnityTest]
        public IEnumerator Stop_DrainsLateEnter_AndRejectsNewSwitch() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe("a") { EnterGate = new UniTaskCompletionSource<bool>() };
            var machine = new ProcedureMachine(new Dictionary<string, IProcedure> { { "a", probe } });
            var entering = machine.SwitchToAsync("a", CancellationToken.None).SuppressCancellationThrow();
            var stopping = machine.StopAsync(CancellationToken.None);
            try
            {
                Assert.AreEqual(UniTaskStatus.Pending, stopping.Status);
                CollectionAssert.AreEqual(new[] { "enter" }, probe.Calls);
                Assert.Throws<ObjectDisposedException>(() =>
                    machine.SwitchToAsync("a", CancellationToken.None).GetAwaiter().GetResult());
            }
            finally { probe.EnterGate.TrySetResult(true); }
            Assert.IsTrue(await entering);
            await stopping;
            Assert.IsNull(machine.CurrentKey);
            CollectionAssert.AreEqual(new[] { "enter", "shutdown" }, probe.Calls);
        });

        [UnityTest]
        public IEnumerator Switch_WhileInFlight_RejectsWithoutOverwriting() => UniTask.ToCoroutine(async () =>
        {
            var probe = new Probe("a") { EnterGate = new UniTaskCompletionSource<bool>() };
            var machine = new ProcedureMachine(new Dictionary<string, IProcedure> { { "a", probe } });
            var entering = machine.SwitchToAsync("a", CancellationToken.None).SuppressCancellationThrow();
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    machine.SwitchToAsync("a", CancellationToken.None).GetAwaiter().GetResult());
            }
            finally { probe.EnterGate.TrySetResult(true); }
            Assert.IsFalse(await entering);
            await machine.StopAsync(CancellationToken.None);
        });

        [UnityTest]
        public IEnumerator Sample_ForwardsScaledDelta_AndComputedRealDelta() => UniTask.ToCoroutine(async () =>
        {
            var clock = new Clock { DeltaTime = 0.25f, RealtimeSinceStartup = 10d };
            var sampler = new ProcedureFrameClock(clock);
            clock.RealtimeSinceStartup = 10.5d;
            sampler.Sample(out float elapsed, out float realElapsed);
            Assert.AreEqual(0.25f, elapsed);
            Assert.AreEqual(0.5f, realElapsed);
            await UniTask.Yield();
        });
    }
}
