using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Rendering;
using Moirai.Atropos.Rendering.Internal;
using NUnit.Framework;

namespace Graphics
{
    public sealed class ResolutionCatalogTests
    {
        private sealed class Logger : ILogger
        {
            internal readonly List<string> Messages = new List<string>();
            public bool IsEnabled(LogLevel level, string tag) => true;
            public void Log(LogLevel level, string tag, string message, Exception exception = null)
            {
                Messages.Add(level + ":" + message);
            }
        }

        private static readonly ResolutionInfo Current = new ResolutionInfo(1920, 1080, 60, 1);
        private static readonly ResolutionInfo Display = new ResolutionInfo(3840, 2160, 60, 1);

        private static ResolutionInfo[] Build(ResolutionInfo[] available, GraphicsOptions options,
            out bool changeSupported, Logger logger = null, bool mobile = false,
            ResolutionInfo? current = null)
        {
            return ResolutionCatalog.Build(available, options, current ?? Current, Display, mobile,
                logger, out changeSupported);
        }

        [Test]
        public void CustomList_WinsOverAvailable()
        {
            var options = new GraphicsOptions
            {
                CustomResolutions = new[] { new ResolutionInfo(1280, 720, 60, 1) },
                LimitToUniqueResolutions = false
            };
            var available = new[] { new ResolutionInfo(800, 600, 60, 1) };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(result.Length, Is.EqualTo(1));
            Assert.That(result[0].Width, Is.EqualTo(1280));
        }

        [Test]
        public void DisplayLimit_DropsOversizedEntries()
        {
            var options = new GraphicsOptions { LimitToUniqueResolutions = false };
            var available = new[]
            {
                new ResolutionInfo(1920, 1080, 60, 1),
                new ResolutionInfo(5120, 2880, 60, 1)
            };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Width, Is.EqualTo(1920));
        }

        [Test]
        public void CurrentRefreshLimit_KeepsOneHertzBand()
        {
            var options = new GraphicsOptions
            {
                LimitToCurrentRefreshRate = true,
                LimitToUniqueResolutions = false
            };
            var available = new[]
            {
                new ResolutionInfo(1920, 1080, 60000, 1001),
                new ResolutionInfo(1920, 1080, 30, 1)
            };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].RateNumerator, Is.EqualTo(60000));
        }

        [Test]
        public void FiftyNineHz_IsDroppedOnlyWhenSixtyExistsForSameSize()
        {
            var options = new GraphicsOptions { LimitToUniqueResolutions = false };
            var available = new[]
            {
                new ResolutionInfo(1920, 1080, 60, 1),
                new ResolutionInfo(1920, 1080, 59000, 1000),
                new ResolutionInfo(1600, 900, 59000, 1000)
            };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(HasRate(result, 1920, 1080, 59d), Is.False);
            Assert.That(HasRate(result, 1600, 900, 59d), Is.True);
        }

        [Test]
        public void AspectFilter_RelaxesAndLogs_WhenEverythingIsFilteredOut()
        {
            var options = new GraphicsOptions
            {
                AllowedAspectRatios = new[] { 1.0d },
                AllowedAspectRatioDelta = 0.01d
            };
            var available = new[] { new ResolutionInfo(1920, 1080, 60, 1) };
            var logger = new Logger();

            ResolutionInfo[] result = Build(available, options, out _, logger);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Width, Is.EqualTo(1920));
            Assert.That(logger.Messages.Count, Is.EqualTo(1));
            Assert.That(logger.Messages[0], Does.Contain("Aspect ratio filter"));
        }

        [Test]
        public void UniqueSizes_KeepRateClosestToCurrent()
        {
            var options = new GraphicsOptions { LimitToUniqueResolutions = true };
            var available = new[]
            {
                new ResolutionInfo(1920, 1080, 120, 1),
                new ResolutionInfo(1920, 1080, 60, 1)
            };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].RateNumerator, Is.EqualTo(60));
        }

        [Test]
        public void Order_PutsExactTargetFirst_ThenNearestByDistance()
        {
            var options = new GraphicsOptions { LimitToUniqueResolutions = false };
            var available = new[]
            {
                new ResolutionInfo(1280, 720, 60, 1),
                new ResolutionInfo(1920, 1080, 60, 1),
                new ResolutionInfo(2560, 1440, 60, 1)
            };

            ResolutionInfo[] result = Build(available, options, out _);

            Assert.That(result[0].Equals(Current), Is.True);
            Assert.That(result[1].Width, Is.EqualTo(2560));
            Assert.That(result[2].Width, Is.EqualTo(1280));
        }

        [Test]
        public void EmptyAvailable_FallsBackToCurrent()
        {
            ResolutionInfo[] result = Build(Array.Empty<ResolutionInfo>(), new GraphicsOptions(), out _);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Equals(Current), Is.True);
        }

        [Test]
        public void Mobile_IsRestrictedUnlessOptionAllows()
        {
            var available = new[] { new ResolutionInfo(1280, 720, 60, 1) };

            ResolutionInfo[] restricted = Build(available, new GraphicsOptions(), out bool supported,
                mobile: true);
            Assert.That(supported, Is.False);
            Assert.That(restricted, Has.Length.EqualTo(1));
            Assert.That(restricted[0].Equals(Current), Is.True);

            var allowing = new GraphicsOptions { AllowResolutionChangeOnMobile = true };
            ResolutionInfo[] allowed = Build(available, allowing, out supported, mobile: true);
            Assert.That(supported, Is.True);
            Assert.That(allowed[0].Width, Is.EqualTo(1280));
        }

        private static bool HasRate(ResolutionInfo[] values, int width, int height, double rate)
        {
            for (int i = 0; i < values.Length; i++)
            {
                ResolutionInfo value = values[i];
                if (value.Width != width || value.Height != height) continue;
                if (Math.Abs((double)value.RateNumerator / value.RateDenominator - rate) <= 0.5d)
                    return true;
            }
            return false;
        }
    }
}
