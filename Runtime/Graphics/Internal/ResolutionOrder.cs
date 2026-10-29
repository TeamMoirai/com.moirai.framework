namespace Moirai.Atropos.Rendering.Internal
{
    internal static class ResolutionOrder
    {
        public static int Compare(ResolutionInfo a, ResolutionInfo b)
        {
            int order = ((long)b.Width * b.Height).CompareTo((long)a.Width * a.Height);
            if (order != 0) return order;
            order = b.Width.CompareTo(a.Width);
            if (order != 0) return order;
            order = b.Height.CompareTo(a.Height);
            if (order != 0) return order;
            return ((long)b.RateNumerator * a.RateDenominator)
                .CompareTo((long)a.RateNumerator * b.RateDenominator);
        }
    }
}
