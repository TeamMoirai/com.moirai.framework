using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 运行时内存明细窗口（按类型过滤的实例级采样，疑似重复实例高亮）。
    /// </summary>
    public sealed class RuntimeMemoryInformationWindow<T> : ScrollableDebuggerWindowBase where T : UnityEngine.Object
    {
        #region 类型 [TYPES]

        /// <summary>
        /// 实例采样记录。
        /// </summary>
        private sealed class Sample
        {
            public readonly string Name;
            public readonly string Type;
            public readonly long Size;
            public bool Highlight;

            public Sample(string name, string type, long size)
            {
                Name = name;
                Type = type;
                Size = size;
            }
        }

        #endregion

        #region 常量 [CONSTANTS]

        private const int SHOW_SAMPLE_COUNT = 300;

        #endregion

        #region 字段 [FIELDS]

        private readonly List<Sample> _samples = new List<Sample>(256);
        private DateTime _sampleTime = DateTime.MinValue;
        private long _sampleSize;
        private long _duplicateSampleSize;
        private int _duplicateSampleCount;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            string typeName = typeof(T).Name;
            VisualElement card = AddSection(root, StringUtility.Format("{0} Runtime Memory Information", typeName));
            card.Add(DebuggerUI.CreateActionButton(StringUtility.Format("Take Sample for {0}", typeName), TakeSample));

            if (_sampleTime <= DateTime.MinValue)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("Please take sample for {0} first.", typeName)));
                return;
            }

            if (_duplicateSampleCount > 0)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                    "{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}, while {4} {1}s ({5}) might be duplicated.",
                    _samples.Count, typeName, DebuggerUI.GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime(),
                    _duplicateSampleCount, DebuggerUI.GetByteLengthString(_duplicateSampleSize))));
            }
            else
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                    "{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}.",
                    _samples.Count, typeName, DebuggerUI.GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime())));
            }

            int count = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                Sample sample = _samples[i];
                string entry = StringUtility.Format("{0}  |  {1}  |  {2}", sample.Name, sample.Type, DebuggerUI.GetByteLengthString(sample.Size));
                VisualElement row = DebuggerUI.CreateRow(StringUtility.Format("#{0}", i + 1), entry);
                if (sample.Highlight)
                {
                    row.AddToClassList("dbg-row--highlight");
                }

                card.Add(row);

                count++;
                if (count >= SHOW_SAMPLE_COUNT)
                {
                    card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("... {0} more samples omitted.", _samples.Count - count)));
                    break;
                }
            }
        }

        #endregion

        #region 采样 [SAMPLING]

        private void TakeSample()
        {
            _sampleTime = DateTime.UtcNow;
            _sampleSize = 0L;
            _duplicateSampleSize = 0L;
            _duplicateSampleCount = 0;
            _samples.Clear();

            T[] samples = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < samples.Length; i++)
            {
                long sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
                _sampleSize += sampleSize;
                _samples.Add(new Sample(samples[i].name, samples[i].GetType().Name, sampleSize));
            }

            _samples.Sort(SampleComparer);

            for (int i = 1; i < _samples.Count; i++)
            {
                if (_samples[i].Name == _samples[i - 1].Name && _samples[i].Type == _samples[i - 1].Type && _samples[i].Size == _samples[i - 1].Size)
                {
                    _samples[i].Highlight = true;
                    _duplicateSampleSize += _samples[i].Size;
                    _duplicateSampleCount++;
                }
            }

            Rebuild();
        }

        private static int SampleComparer(Sample a, Sample b)
        {
            int result = b.Size.CompareTo(a.Size);
            if (result != 0)
            {
                return result;
            }

            result = string.CompareOrdinal(a.Type, b.Type);
            if (result != 0)
            {
                return result;
            }

            return string.CompareOrdinal(a.Name, b.Name);
        }

        #endregion
    }
}
