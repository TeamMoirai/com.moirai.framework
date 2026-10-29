using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Logging;

namespace Moirai.Atropos.Rendering.Internal
{
    internal static class ResolutionCatalog
    {
        private const double REFRESH_TOLERANCE = 1d;
        private const double RATE_59_LOW = 58.5d;
        private const double RATE_59_HIGH = 59.5d;
        private const double RATE_60_LOW = 59.5d;
        private const double RATE_60_HIGH = 60.5d;

        internal static ResolutionInfo[] Build(IReadOnlyList<ResolutionInfo> available,
            GraphicsOptions options, ResolutionInfo current, ResolutionInfo displayLimit,
            bool isMobile, ILogger logger, out bool changeSupported)
        {
            changeSupported = !isMobile || options.AllowResolutionChangeOnMobile;
            if (!changeSupported) return new[] { current };

            IReadOnlyList<ResolutionInfo> source = options.CustomResolutions.Length > 0
                ? options.CustomResolutions : available;
            var candidates = new List<ResolutionInfo>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ResolutionInfo entry = source[i];
                if (options.LimitMaxResolutionToDisplayResolution &&
                    (entry.Width > displayLimit.Width || entry.Height > displayLimit.Height)) continue;
                if (options.LimitToCurrentRefreshRate &&
                    !WithinTolerance(entry, current, REFRESH_TOLERANCE)) continue;
                candidates.Add(entry);
            }

            if (options.SkipRefreshRatesWith59Hz && !options.LimitToCurrentRefreshRate)
                DropFiftyNine(candidates);

            if (options.AllowedAspectRatios.Length > 0)
            {
                var filtered = new List<ResolutionInfo>(candidates.Count);
                for (int i = 0; i < candidates.Count; i++)
                    if (MatchesAspect(candidates[i], options)) filtered.Add(candidates[i]);
                if (filtered.Count == 0)
                    logger?.Log(LogLevel.Warning, nameof(ResolutionCatalog),
                        "Aspect ratio filter removed every resolution; keeping the unfiltered list.");
                else
                    candidates = filtered;
            }

            if (options.LimitToUniqueResolutions) KeepClosestPerSize(candidates, current);
            candidates.Sort(ResolutionOrder.Compare);
            if (candidates.Count == 0) return new[] { current };

            MoveExactTargetFirst(candidates, current);
            var ordered = new List<ResolutionInfo>(candidates.Count);
            ordered.AddRange(candidates);
            int targetWidth = current.Width;
            int targetHeight = current.Height;
            ordered.Sort((a, b) =>
            {
                int distance = Distance(a, targetWidth, targetHeight)
                    .CompareTo(Distance(b, targetWidth, targetHeight));
                return distance != 0 ? distance : ResolutionOrder.Compare(a, b);
            });
            MoveExactTargetFirst(ordered, current);
            return ordered.ToArray();
        }

        private static bool WithinTolerance(ResolutionInfo entry, ResolutionInfo current, double seconds)
        {
            return Math.Abs(Rate(entry) - Rate(current)) <= seconds;
        }

        private static double Rate(ResolutionInfo value)
            => (double)value.RateNumerator / value.RateDenominator;

        private static void DropFiftyNine(List<ResolutionInfo> candidates)
        {
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                ResolutionInfo entry = candidates[i];
                double rate = Rate(entry);
                if (rate < RATE_59_LOW || rate >= RATE_59_HIGH) continue;
                bool hasSixty = false;
                for (int j = 0; j < candidates.Count; j++)
                {
                    ResolutionInfo other = candidates[j];
                    double otherRate = Rate(other);
                    if (other.Width == entry.Width && other.Height == entry.Height &&
                        otherRate >= RATE_60_LOW && otherRate <= RATE_60_HIGH)
                    {
                        hasSixty = true;
                        break;
                    }
                }
                if (hasSixty) candidates.RemoveAt(i);
            }
        }

        private static bool MatchesAspect(ResolutionInfo entry, GraphicsOptions options)
        {
            double ratio = (double)entry.Width / entry.Height;
            for (int i = 0; i < options.AllowedAspectRatios.Length; i++)
                if (Math.Abs(ratio - options.AllowedAspectRatios[i]) <= options.AllowedAspectRatioDelta)
                    return true;
            return false;
        }

        private static void KeepClosestPerSize(List<ResolutionInfo> candidates, ResolutionInfo current)
        {
            var best = new Dictionary<long, ResolutionInfo>(candidates.Count);
            var order = new List<long>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ResolutionInfo entry = candidates[i];
                long key = ((long)entry.Width << 32) | (uint)entry.Height;
                if (!best.TryGetValue(key, out ResolutionInfo existing))
                {
                    best.Add(key, entry);
                    order.Add(key);
                    continue;
                }
                double existingDistance = Math.Abs(Rate(existing) - Rate(current));
                double entryDistance = Math.Abs(Rate(entry) - Rate(current));
                if (entryDistance < existingDistance ||
                    (entryDistance == existingDistance && Rate(entry) > Rate(existing)))
                    best[key] = entry;
            }
            candidates.Clear();
            for (int i = 0; i < order.Count; i++) candidates.Add(best[order[i]]);
        }

        private static void MoveExactTargetFirst(List<ResolutionInfo> candidates, ResolutionInfo current)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].Equals(current)) continue;
                ResolutionInfo exact = candidates[i];
                candidates.RemoveAt(i);
                candidates.Insert(0, exact);
                return;
            }
        }

        private static int Distance(ResolutionInfo value, int width, int height)
            => Math.Abs(value.Width - width) + Math.Abs(value.Height - height);
    }
}
