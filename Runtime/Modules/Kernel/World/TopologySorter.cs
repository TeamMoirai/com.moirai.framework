using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务依赖图拓扑排序器（Kahn 算法）。世界初始化期一次性使用，非热路径。
    /// <para>确定性：同入度节点按注册顺序出队（稳定序），保证同图同序。</para>
    /// <para>缺失依赖与循环依赖在此 fail-fast——错误消息含完整剩余环成员。</para>
    /// </summary>
    internal static class TopologySorter
    {
        /// <summary>
        /// 对待初始化服务按 <c>[ServiceDependency]</c> 声明做拓扑排序。
        /// </summary>
        /// <param name="pending">待初始化服务（注册顺序）。排序结果写回本列表。</param>
        /// <param name="pendingIndexByContract">契约类型 → pending 索引（含服务的全部契约，不止实现类型）。</param>
        /// <param name="dependenciesOf">解析服务实现类型声明的依赖数组。</param>
        /// <param name="isServiceReady">判定某契约类型是否已初始化就绪（已就绪依赖不构成边）。</param>
        /// <exception cref="GameException">存在未注册依赖或循环依赖时抛出。</exception>
        internal static void Sort(
            List<IService> pending,
            Dictionary<Type, int> pendingIndexByContract,
            Func<Type, Type[]> dependenciesOf,
            Func<Type, bool> isServiceReady)
        {
            int count = pending.Count;
            // 单服务同样走完整校验——缺失依赖必须 fail-fast，不能因图小而跳过
            if (count == 0) return;

            var inDegree = new int[count];
            var edges = new List<int>[count];

            for (int i = 0; i < count; i++)
            {
                Type[] deps = dependenciesOf(pending[i].GetType());
                for (int d = 0; d < deps.Length; d++)
                {
                    Type dep = deps[d];
                    if (isServiceReady(dep)) continue;

                    if (!pendingIndexByContract.TryGetValue(dep, out int depIndex))
                    {
                        throw new GameException(StringUtility.Format(
                            "Dependency '{0}' required by '{1}' is not registered. Register it before world initialization.",
                            dep.FullName, pending[i].GetType().FullName));
                    }

                    (edges[depIndex] ??= new List<int>()).Add(i);
                    inDegree[i]++;
                }
            }

            // Kahn：稳定出队（注册序最小者优先），结果写回 pending
            var sorted = new List<IService>(count);
            var ready = new List<int>(count);
            for (int i = 0; i < count; i++)
                if (inDegree[i] == 0) ready.Add(i);

            while (ready.Count > 0)
            {
                int node = ready[0];
                ready.RemoveAt(0);
                sorted.Add(pending[node]);

                var outs = edges[node];
                if (outs == null) continue;
                for (int k = 0; k < outs.Count; k++)
                {
                    int target = outs[k];
                    if (--inDegree[target] != 0) continue;

                    int insertAt = ready.Count;
                    for (int r = 0; r < ready.Count; r++)
                    {
                        if (ready[r] > target) { insertAt = r; break; }
                    }
                    ready.Insert(insertAt, target);
                }
            }

            if (sorted.Count != count)
            {
                var cycle = new List<string>();
                for (int i = 0; i < count; i++)
                    if (inDegree[i] > 0) cycle.Add(pending[i].GetType().FullName);

                throw new GameException(StringUtility.Format(
                    "Circular service dependency detected among: {0}",
                    string.Join(" -> ", cycle)));
            }

            pending.Clear();
            pending.AddRange(sorted);
        }
    }
}
