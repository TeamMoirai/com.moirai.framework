using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// Profiler 摘要窗口。
    /// </summary>
    public sealed class ProfilerInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Profiler Information");
            AddRow(card, "Supported", Profiler.supported.ToString());
            AddRow(card, "Enabled", Profiler.enabled.ToString());
            AddRow(card, "Enable Binary Log", Profiler.enableBinaryLog ? StringUtility.Format("True, {0}", Profiler.logFile) : "False");
            AddRow(card, "Enable Allocation Callstacks", Profiler.enableAllocationCallstacks.ToString());
            AddRow(card, "Area Count", Profiler.areaCount.ToString());
            AddRow(card, "Max Used Memory", DebuggerUI.GetByteLengthString(Profiler.maxUsedMemory));
            AddRow(card, "Mono Used Size", DebuggerUI.GetByteLengthString(Profiler.GetMonoUsedSizeLong()));
            AddRow(card, "Mono Heap Size", DebuggerUI.GetByteLengthString(Profiler.GetMonoHeapSizeLong()));
            AddRow(card, "Used Heap Size", DebuggerUI.GetByteLengthString(Profiler.usedHeapSizeLong));
            AddRow(card, "Total Allocated Memory", DebuggerUI.GetByteLengthString(Profiler.GetTotalAllocatedMemoryLong()));
            AddRow(card, "Total Reserved Memory", DebuggerUI.GetByteLengthString(Profiler.GetTotalReservedMemoryLong()));
            AddRow(card, "Total Unused Reserved Memory", DebuggerUI.GetByteLengthString(Profiler.GetTotalUnusedReservedMemoryLong()));
            AddRow(card, "Allocated Memory For Graphics Driver", DebuggerUI.GetByteLengthString(Profiler.GetAllocatedMemoryForGraphicsDriver()));
            AddRow(card, "Temp Allocator Size", DebuggerUI.GetByteLengthString(Profiler.GetTempAllocatorSize()));
            AddRow(card, "Marshal Cached HGlobal Size", DebuggerUI.GetByteLengthString(MarshalUtility.CachedHGlobalSize));
        }

        #endregion
    }
}
