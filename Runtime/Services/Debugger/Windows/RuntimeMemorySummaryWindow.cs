using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 运行时内存摘要窗口（按类型聚合的采样统计）。
    /// </summary>
    public sealed class RuntimeMemorySummaryWindow : ScrollableDebuggerWindowBase
    {
        #region 类型 [TYPES]

        /// <summary>
        /// 类型聚合记录。
        /// </summary>
        private sealed class Record
        {
            public readonly string Name;
            public int Count;
            public long Size;

            public Record(string name)
            {
                Name = name;
            }
        }

        #endregion

        #region 字段 [FIELDS]

        private readonly List<Record> _records = new List<Record>(128);
        private readonly Dictionary<string, Record> _recordByName = new Dictionary<string, Record>(128);
        private DateTime _sampleTime = DateTime.MinValue;
        private int _sampleCount;
        private long _sampleSize;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Runtime Memory Summary");
            card.Add(DebuggerUI.CreateActionButton("Take Sample", TakeSample));

            if (_sampleTime <= DateTime.MinValue)
            {
                card.Add(DebuggerUI.CreateHintLabel("Please take sample first."));
                return;
            }

            card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("{0} Objects ({1}) obtained at {2:yyyy-MM-dd HH:mm:ss}.",
                _sampleCount, DebuggerUI.GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime())));

            for (int i = 0; i < _records.Count; i++)
            {
                AddRow(card, _records[i].Name, StringUtility.Format("{0}  |  {1}", _records[i].Count, DebuggerUI.GetByteLengthString(_records[i].Size)));
            }
        }

        #endregion

        #region 采样 [SAMPLING]

        private void TakeSample()
        {
            _records.Clear();
            _recordByName.Clear();
            _sampleTime = DateTime.UtcNow;
            _sampleCount = 0;
            _sampleSize = 0L;

            UnityEngine.Object[] samples = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            for (int i = 0; i < samples.Length; i++)
            {
                long sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
                string name = samples[i].GetType().Name;
                _sampleCount++;
                _sampleSize += sampleSize;

                if (!_recordByName.TryGetValue(name, out Record record))
                {
                    record = new Record(name);
                    _recordByName[name] = record;
                    _records.Add(record);
                }

                record.Count++;
                record.Size += sampleSize;
            }

            _records.Sort(RecordComparer);
            Rebuild();
        }

        private static int RecordComparer(Record a, Record b)
        {
            int result = b.Size.CompareTo(a.Size);
            if (result != 0)
            {
                return result;
            }

            result = a.Count.CompareTo(b.Count);
            if (result != 0)
            {
                return result;
            }

            return string.CompareOrdinal(a.Name, b.Name);
        }

        #endregion
    }
}
