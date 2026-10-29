using System;
using Moirai.Atropos.Storage;
using NUnit.Framework;

namespace Storage
{
    public sealed class StorageRequirementsTests
    {
        [Test]
        public void Ensure_WhenAllCapabilitiesPresent_Passes()
        {
            StorageCapabilities available = StorageCapabilities.AsyncRead |
                StorageCapabilities.AsyncWrite | StorageCapabilities.AtomicCommit;

            Assert.DoesNotThrow(() => StorageRequirements.Ensure(available,
                StorageCapabilities.AsyncWrite | StorageCapabilities.AtomicCommit, "settings"));
        }

        [Test]
        public void Ensure_WhenCapabilityMissing_ThrowsNamingIt()
        {
            StorageCapabilities available = StorageCapabilities.AsyncRead;

            var error = Assert.Throws<NotSupportedException>(() => StorageRequirements.Ensure(
                available, StorageCapabilities.AsyncRead | StorageCapabilities.AtomicCommit, "save"));

            Assert.That(error.Message, Does.Contain("save"));
            Assert.That(error.Message, Does.Contain(nameof(StorageCapabilities.AtomicCommit)));
            Assert.That(error.Message, Does.Not.Contain(nameof(StorageCapabilities.AsyncRead)));
        }

        [Test]
        public void Ensure_NoRequirement_PassesEvenWithNothing()
        {
            Assert.DoesNotThrow(() => StorageRequirements.Ensure(StorageCapabilities.None,
                StorageCapabilities.None, "read-only view"));
        }

        [Test]
        public void Ensure_NullFeature_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => StorageRequirements.Ensure(
                StorageCapabilities.None, StorageCapabilities.None, null));
        }
    }
}
