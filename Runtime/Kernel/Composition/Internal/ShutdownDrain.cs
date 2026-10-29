using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Kernel.Composition.Internal
{
    internal static class ShutdownDrain
    {
        internal static async UniTask ObserveAllAsync(IReadOnlyList<UniTask> operations, List<Exception> failures)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                try { await operations[i]; }
                catch (Exception error) { failures.Add(error); }
            }
        }
    }
}
