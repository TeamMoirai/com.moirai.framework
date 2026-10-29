using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Backends;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Plugins;
using Moirai.Atropos.Plugins.Internal;
using NUnit.Framework;

namespace Plugins
{
    public sealed class PluginContextTests
    {
        private sealed class Services : IPluginServices
        {
            private sealed class FakeClock : IClock
            {
                public float Time => 0f;
                public float DeltaTime => 0f;
                public double RealtimeSinceStartup => 0d;
            }
            private sealed class FakeLogger : ILogger
            {
                public bool IsEnabled(LogLevel level, string tag) => true;
                public void Log(LogLevel level, string tag, string message, Exception exception = null) { }
            }
            public ILogger Logger { get; } = new FakeLogger();
            public IClock Clock { get; } = new FakeClock();
        }

        private sealed class Auth : IAuthBackend
        {
            internal int Calls;
            public UniTask<AuthResult> SignInAsync(SignInRequest request, CallOptions call,
                CancellationToken cancellationToken)
            {
                Calls++;
                return UniTask.FromResult(new AuthResult(AuthStatus.Authenticated, "token"));
            }
        }

        private static CallOptions Call() => new CallOptions("trace", DateTimeOffset.MaxValue, "key");

        [Test]
        public void UnauthorizedCapability_ThrowsBeforeBackendCall()
        {
            var auth = new Auth();
            var context = new ScopedPluginContext(new PluginId("p"), PluginCapability.Resource,
                new Services(), CancellationToken.None, auth);
            Assert.Throws<UnauthorizedAccessException>(() => _ = context.Auth);
            Assert.AreEqual(0, auth.Calls);
        }

        [Test]
        public void GrantedCapability_DelegatesToBackend()
        {
            var auth = new Auth();
            var context = new ScopedPluginContext(new PluginId("p"), PluginCapability.Auth,
                new Services(), CancellationToken.None, auth);
            UniTask<AuthResult> result = context.Auth.SignInAsync(new SignInRequest("a", "b"),
                Call(), CancellationToken.None);
            Assert.AreEqual(1, auth.Calls);
            Assert.IsTrue(result.GetAwaiter().GetResult().Succeeded);
        }

        [Test]
        public void InvalidatedContext_RejectsFurtherAccess()
        {
            var auth = new Auth();
            var context = new ScopedPluginContext(new PluginId("p"), PluginCapability.Auth,
                new Services(), CancellationToken.None, auth);
            context.Invalidate();
            Assert.Throws<ObjectDisposedException>(() => _ = context.Auth);
            Assert.AreEqual(0, auth.Calls);
        }
    }
}
