using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Plugins
{
    public sealed class PluginDescriptor
    {
        public PluginDescriptor(PluginId id, PluginVersion version,
            PluginCapability requiredCapabilities, Func<IPlugin> factory)
        {
            Id = id;
            Version = version;
            RequiredCapabilities = requiredCapabilities;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public PluginId Id { get; }
        public PluginVersion Version { get; }
        public PluginCapability RequiredCapabilities { get; }
        public Func<IPlugin> Factory { get; }
    }

    public sealed class PluginCatalog : IPluginCatalog
    {
        private readonly List<Func<IPlugin>> _factories = new List<Func<IPlugin>>();

        public PluginCatalog(PluginCapability grantedCapabilities,
            IEnumerable<PluginDescriptor> descriptors)
        {
            GrantedCapabilities = grantedCapabilities;
            if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
            var seen = new HashSet<PluginId>();
            foreach (PluginDescriptor descriptor in descriptors)
            {
                if (descriptor == null) throw new ArgumentException("A plugin descriptor cannot be null.");
                if (!seen.Add(descriptor.Id))
                    throw new InvalidOperationException("Duplicate plugin id: " + descriptor.Id);
                if ((descriptor.RequiredCapabilities & ~grantedCapabilities) != 0)
                {
                    throw new InvalidOperationException(
                        "Plugin '" + descriptor.Id + "' requires capabilities that were not granted.");
                }
                _factories.Add(descriptor.Factory);
            }
        }

        public PluginCapability GrantedCapabilities { get; }
        public IReadOnlyList<Func<IPlugin>> Factories => _factories;
    }
}
