using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Bootstrap.Internal
{
    internal static class BootDrain
    {
        internal static async UniTask<Exception[]> ObserveAllAsync(IReadOnlyList<UniTask> owned)
        {
            var failures = new List<Exception>();
            for (int i = 0; i < owned.Count; i++)
            {
                try { await owned[i]; }
                catch (Exception failure) { failures.Add(failure); }
            }
            return failures.ToArray();
        }
    }
}
