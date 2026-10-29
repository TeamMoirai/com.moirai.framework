using System;
using Moirai.Atropos.Rendering;
using Moirai.Atropos.Rendering.Internal;
using NUnit.Framework;

namespace Graphics
{
    public sealed class ResolutionOrderTests
    {
        [Test]
        public void EqualArea_SortsWiderFirst_AndKeepsRationalRefreshRate()
        {
            var wide = new ResolutionInfo(1920, 1080, 60000, 1001);
            var tall = new ResolutionInfo(1080, 1920, 60, 1);
            var values = new[] { tall, wide };

            Array.Sort(values, ResolutionOrder.Compare);

            Assert.That(values[0].Width, Is.EqualTo(1920));
            Assert.That((double)values[0].RateNumerator / values[0].RateDenominator,
                Is.EqualTo(60000d / 1001d).Within(0.000001d));
            Assert.That(ResolutionOrder.Compare(new ResolutionInfo(1920, 1080, 60, 1),
                new ResolutionInfo(1920, 1080, 120, 2)), Is.Zero);
        }

        [Test]
        public void Compare_OrdersByPixelsThenRate()
        {
            var large = new ResolutionInfo(2560, 1440, 60, 1);
            var small = new ResolutionInfo(1920, 1080, 144, 1);
            var slow = new ResolutionInfo(1920, 1080, 60, 1);

            Assert.That(ResolutionOrder.Compare(large, small), Is.LessThan(0));
            Assert.That(ResolutionOrder.Compare(slow, small), Is.GreaterThan(0));
        }

        [Test]
        public void Compare_LargeAreas_DoNotOverflow()
        {
            var big = new ResolutionInfo(60000, 60000, 60, 1);
            var medium = new ResolutionInfo(50000, 50000, 60, 1);

            Assert.That(ResolutionOrder.Compare(big, medium), Is.LessThan(0));
            Assert.That(ResolutionOrder.Compare(medium, big), Is.GreaterThan(0));
        }

        [Test]
        public void Encode_UsesStableInvariantForm()
        {
            Assert.That(ResolutionPreference.Encode(new ResolutionInfo(1920, 1080, 60000, 1001)),
                Is.EqualTo("1920x1080@60000/1001"));
        }

        [Test]
        public void Decode_RoundTripsEncode()
        {
            var resolution = new ResolutionInfo(1280, 720, 30000, 1001);

            ResolutionInfo decoded = ResolutionPreference.Decode(ResolutionPreference.Encode(resolution));

            Assert.That(decoded.Width, Is.EqualTo(1280));
            Assert.That(decoded.Height, Is.EqualTo(720));
            Assert.That(decoded.RateNumerator, Is.EqualTo(30000));
            Assert.That(decoded.RateDenominator, Is.EqualTo(1001));
        }

        [TestCase("")]
        [TestCase("1920x1080")]
        [TestCase("1920x1080@60")]
        [TestCase("1920x1080@60/1/1")]
        [TestCase("0x1080@60/1")]
        [TestCase("-1920x1080@60/1")]
        [TestCase("1920x0@60/1")]
        [TestCase("1920x1080@0/1")]
        [TestCase("1920x1080@60/0")]
        [TestCase("1920x1080@-60/1")]
        [TestCase("axb@c/d")]
        public void Decode_RejectsMalformedOrOutOfRange(string value)
        {
            Assert.Throws<FormatException>(() => ResolutionPreference.Decode(value));
        }

        [Test]
        public void Decode_NullIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => ResolutionPreference.Decode(null));
        }
    }
}
