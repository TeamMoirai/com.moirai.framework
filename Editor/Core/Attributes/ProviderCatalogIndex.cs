using System;
using System.Collections.Generic;
using System.Linq;
using Moirai.Atropos.Editor;
using UnityEditor;

namespace Moirai.Atropos
{
    /// <summary>Provider 下拉一行的可用性。</summary>
    internal enum ProviderRowState : byte
    {
        /// <summary>可直接选中。</summary>
        Usable = 0,
        /// <summary>提供它的插件还没装。</summary>
        PluginNotInstalled = 1,
        /// <summary>插件装了但实现类不在（SDK 缺、宏未开），或类型自述不可用。</summary>
        BackendUnavailable = 2
    }

    /// <summary>
    /// Provider 下拉的一行：显示名、详情文案、可选类型与可用性。
    /// </summary>
    internal readonly struct ProviderRow
    {
        internal readonly string Title;
        internal readonly string Description;
        internal readonly Type Candidate;
        internal readonly ProviderRowState State;

        internal ProviderRow(string title, string description, Type candidate, ProviderRowState state)
        {
            Title = title;
            Description = description;
            Candidate = candidate;
            State = state;
        }

        /// <summary>是否允许写值：不可选行一律 <c>false</c>，其 <see cref="Candidate"/> 恒为 null。</summary>
        internal bool Selectable => Candidate != null && State == ProviderRowState.Usable;
    }

    /// <summary>
    /// 把「已编译候选 + 目录缓存清单 + 账本已装 id」归成下拉行序列。
    /// </summary>
    /// <remarks>
    /// <see cref="Build"/> 是纯函数，判据可脱离 IMGUI、磁盘与 git 单测；<see cref="ForField"/> 才取真状态。<br />
    /// 同一后端只会出现在一行，去重看类型清单而不看台账：只要 <c>providerType</c> 已编译进域，清单行就不再铺，
    /// 由编译候选（或自述不可用的类型）接管——已编译的候选才是权威。<br />
    /// 台账只决定这一行报「未安装」还是「未生效」，不再决定要不要铺它；<c>providerType</c> 缺席时无法判「已在场」，
    /// 清单行照铺，也绝不拿它去查类型集合。
    /// </remarks>
    internal static class ProviderCatalogIndex
    {
        /// <summary>
        /// 归行。<paramref name="compiled"/> 须已按 <c>Name</c> 的 <c>Ordinal</c> 排好序（沿用抽屉既有约定）。
        /// </summary>
        internal static ProviderRow[] Build(Type baseType, Type[] compiled,
            IReadOnlyList<PluginManifest> catalog, IReadOnlyCollection<string> installedIds)
        {
            var rows = new List<ProviderRow>();
            var loadedTypeNames = new HashSet<string>(StringComparer.Ordinal);

            if (compiled != null)
            {
                for (int i = 0; i < compiled.Length; i++)
                {
                    Type candidate = compiled[i];
                    loadedTypeNames.Add(candidate.FullName);

                    var display = candidate.GetCustomAttributes(typeof(ProviderDisplayAttribute), false)
                        .FirstOrDefault() as ProviderDisplayAttribute;
                    var unavailable = candidate.GetCustomAttributes(typeof(ProviderUnavailableAttribute), false)
                        .FirstOrDefault() as ProviderUnavailableAttribute;
                    string title = !string.IsNullOrEmpty(display?.Title) ? display.Title : candidate.Name;

                    if (unavailable != null)
                        rows.Add(new ProviderRow(title, unavailable.Reason, null, ProviderRowState.BackendUnavailable));
                    else
                        rows.Add(new ProviderRow(title, display?.Description, candidate, ProviderRowState.Usable));
                }
            }

            if (catalog == null) return rows.ToArray();

            for (int i = 0; i < catalog.Count; i++)
            {
                PluginManifest manifest = catalog[i];
                if (manifest?.providers == null) continue;
                bool installed = installedIds != null && installedIds.Contains(manifest.id);

                for (int p = 0; p < manifest.providers.Length; p++)
                {
                    PluginProvider provider = manifest.providers[p];
                    if (provider == null || !string.Equals(provider.providerBase, baseType.FullName, StringComparison.Ordinal))
                        continue;
                    // 去重只看类型清单，不看台账：类型已编译进域就以候选为准，清单行不再铺。
                    // 判据反过来（先看 installed）会让「包内嵌在 Packages/ 里、账本没记这条 id」的后端同时冒出
                    // 一条 Usable 候选与一条假「未安装」行。
                    // 非空判断必须在 Contains 之前：清单来自账本缓存或手工夹具时不保证 providerType 非空，
                    // 而 null 进类型集合的查询在不同运行时实现下可能直接抛（HashSet 的比较器不保证吞 null）。
                    if (!string.IsNullOrEmpty(provider.providerType) && loadedTypeNames.Contains(provider.providerType))
                        continue;

                    // requiresUpm 的「空」有两种写法：键缺席与显式空串，二者同义，都按「只随本插件本体」给文案，
                    // 所以这里判 IsNullOrEmpty 而不是 != null（判 != null 会把空串当成有外部依赖）。
                    string title = !string.IsNullOrEmpty(provider.title) ? provider.title : manifest.name;
                    // C# 9 不收非逐字内插串里的换行，故把两段文案先落成局部量再拼。
                    string missingWhy = string.IsNullOrEmpty(provider.requiresUpm)
                        ? "本插件的程序集里没有该实现类"
                        : $"需要 UPM 包 {provider.requiresUpm}，装好后重开下拉";
                    string installTail = string.IsNullOrEmpty(provider.requiresUpm)
                        ? string.Empty
                        : $"，它会带上 UPM 包 {provider.requiresUpm}";
                    string reason = installed
                        ? $"{title} 未生效：{missingWhy}"
                        : $"未安装：到 Tools ▸ Moirai ▸ Package Manager 装「{manifest.name}」（{manifest.id}）" + installTail;

                    rows.Add(new ProviderRow(title, reason, null,
                        installed ? ProviderRowState.BackendUnavailable : ProviderRowState.PluginNotInstalled));
                }
            }

            return rows.ToArray();
        }

        /// <summary>该行可否写值：可写返本行下标，不可写返 <c>-1</c>（越界同样返 -1）。</summary>
        internal static int ActivationIndex(ProviderRow[] rows, int rowIndex) =>
            rows != null && rowIndex >= 0 && rowIndex < rows.Length && rows[rowIndex].Selectable ? rowIndex : -1;

        /// <summary>
        /// 编译候选的筛选项：基类的非抽象实现类，排除测试装配，按 <c>Name</c> 的 <c>Ordinal</c> 排序。
        /// </summary>
        /// <remarks>
        /// 这是候选筛选的唯一真相：<see cref="ForField"/> 与 Provider 下拉的类型菜单缓存都调它。
        /// 两处各写一遍的话筛选项会分叉——一边加了豁免、另一边不知道，下拉与判据层看到的候选集就不再同源。
        /// </remarks>
        internal static Type[] CompiledCandidates(Type baseType)
        {
            return TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract
                    && !t.Assembly.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal)
                    && !t.Assembly.GetName().Name.Contains(".Tests."))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>取真状态归行：编译候选 + 包管理器目录缓存 + 已装账本。</summary>
        /// <remarks>
        /// 前置条件：<b>调用方必须按 <paramref name="baseType"/> 缓存结果</b>（每个域一次，并在程序集重载前失效）。<br />
        /// 本方法每趟做一次账本盘 IO + 反序列化，并逐条拷贝清单——放进每次绘制或每个字段的循环，就是面板每帧一次磁盘读。
        /// </remarks>
        internal static ProviderRow[] ForField(Type baseType)
        {
            Type[] compiled = CompiledCandidates(baseType);

            PackageManagerStateData state = PackageManagerStateRegistry.Load();
            List<PluginManifest> catalog = null;
            List<string> installedIds = null;

            if (state != null && PackageManagerStateRegistry.TryGetCachedCatalog(state, out catalog, out _))
            {
                installedIds = new List<string>();
                for (int i = 0; i < state.installed.Count; i++)
                    if (state.installed[i] != null && !string.IsNullOrEmpty(state.installed[i].id))
                        installedIds.Add(state.installed[i].id);
            }

            return Build(baseType, compiled, catalog, installedIds);
        }
    }
}
