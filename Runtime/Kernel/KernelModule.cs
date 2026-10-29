using System;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Events;
using Moirai.Atropos.Kernel.Events.Internal;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Logging.Internal;

namespace Moirai.Atropos.Kernel
{
    public sealed class KernelModule : IServiceModule
    {
        private readonly ILogSink[] _sinks;
        private readonly LoggerOptions _options;

        public KernelModule(ILogSink[] sinks, LoggerOptions options)
        {
            _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Register(IServiceRegistrar registrar)
        {
            registrar.Add(new ServiceDescriptor(typeof(ILogger), Type.EmptyTypes,
                ServiceLifetime.Root, _ => new Logger(_sinks, _options)));
            registrar.Add(new ServiceDescriptor(typeof(IEventBus), new[] { typeof(ILogger) },
                ServiceLifetime.Root,
                static provider => new EventBus(
                    (ILogger)provider.GetService(typeof(ILogger)))));
        }
    }
}
