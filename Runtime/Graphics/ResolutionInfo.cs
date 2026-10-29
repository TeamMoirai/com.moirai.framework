using System;

namespace Moirai.Atropos.Rendering
{
    public readonly struct ResolutionInfo : IEquatable<ResolutionInfo>
    {
        public ResolutionInfo(int width, int height, int rateNumerator, int rateDenominator)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (rateNumerator <= 0) throw new ArgumentOutOfRangeException(nameof(rateNumerator));
            if (rateDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(rateDenominator));
            Width = width;
            Height = height;
            RateNumerator = rateNumerator;
            RateDenominator = rateDenominator;
        }

        public int Width { get; }
        public int Height { get; }
        public int RateNumerator { get; }
        public int RateDenominator { get; }

        public bool Equals(ResolutionInfo other) =>
            Width == other.Width && Height == other.Height &&
            RateNumerator == other.RateNumerator && RateDenominator == other.RateDenominator;

        public override bool Equals(object obj) => obj is ResolutionInfo other && Equals(other);
        public override int GetHashCode() =>
            ((Width * 397 ^ Height) * 397 ^ RateNumerator) * 397 ^ RateDenominator;
        public override string ToString() =>
            Width + "x" + Height + "@" + RateNumerator + "/" + RateDenominator;
    }
}
