using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class MemoryPoolPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            private readonly Dictionary<string, List<MemoryPoolInfo>> _memoryPoolInfos = new Dictionary<string, List<MemoryPoolInfo>>(StringComparer.Ordinal);
            private readonly Comparison<MemoryPoolInfo> _normalClassNameComparer = NormalClassNameComparer;
            private readonly Comparison<MemoryPoolInfo> _fullClassNameComparer = FullClassNameComparer;
            private MemoryPoolInfo[] _infoBuffer = Array.Empty<MemoryPoolInfo>();
            private bool _showFullClassName = false;

            public override void Initialize(params object[] args)
            {
            }

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Memory Pool Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Memory Pool Count", MemoryPool.Count.ToString());
                    DrawItem("Phase", MemoryPoolRegistry.Phase.ToString());
                }
                GUILayout.EndVertical();

                _showFullClassName = GUILayout.Toggle(_showFullClassName, "Show Full Class Name");
                _memoryPoolInfos.Clear();

                int count = MemoryPool.Count;
                if (_infoBuffer.Length < count)
                {
                    _infoBuffer = new MemoryPoolInfo[count];
                }

                int actualCount = MemoryPool.GetAllMemoryPoolInfos(_infoBuffer);
                for (int i = 0; i < actualCount; i++)
                {
                    MemoryPoolInfo memoryPoolInfo = _infoBuffer[i];
                    string assemblyName = memoryPoolInfo.Type.Assembly.GetName().Name;
                    List<MemoryPoolInfo> results = null;
                    if (!_memoryPoolInfos.TryGetValue(assemblyName, out results))
                    {
                        results = new List<MemoryPoolInfo>();
                        _memoryPoolInfos.Add(assemblyName, results);
                    }

                    results.Add(memoryPoolInfo);
                }

                foreach (KeyValuePair<string, List<MemoryPoolInfo>> assemblyMemoryPoolInfo in _memoryPoolInfos)
                {
                    GUILayout.Label(StringUtility.Format("<b>Assembly: {0}</b>", assemblyMemoryPoolInfo.Key));
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Label(_showFullClassName ? "<b>Full Class Name</b>" : "<b>Class Name</b>");
                            GUILayout.Label("<b>Unused</b>", GUILayout.Width(50f));
                            GUILayout.Label("<b>Using</b>", GUILayout.Width(50f));
                            GUILayout.Label("<b>Acquire</b>", GUILayout.Width(55f));
                            GUILayout.Label("<b>Release</b>", GUILayout.Width(55f));
                            GUILayout.Label("<b>Miss</b>", GUILayout.Width(55f));
                            GUILayout.Label("<b>Reserve</b>", GUILayout.Width(55f));
                            GUILayout.Label("<b>Idle</b>", GUILayout.Width(50f));
                            GUILayout.Label("<b>Pages</b>", GUILayout.Width(50f));
                            GUILayout.Label("<b>Util%</b>", GUILayout.Width(45f));
                        }
                        GUILayout.EndHorizontal();

                        if (assemblyMemoryPoolInfo.Value.Count > 0)
                        {
                            assemblyMemoryPoolInfo.Value.Sort(_showFullClassName ? _fullClassNameComparer : _normalClassNameComparer);
                            foreach (MemoryPoolInfo memoryPoolInfo in assemblyMemoryPoolInfo.Value)
                            {
                                DrawMemoryPoolInfo(memoryPoolInfo);
                            }
                        }
                        else
                        {
                            GUILayout.Label("<i>Memory Pool is Empty ...</i>");
                        }
                    }
                    GUILayout.EndVertical();
                }
            }

            private void DrawMemoryPoolInfo(MemoryPoolInfo info)
            {
                int pageCapacity = info.PageCapacity;
                int utilPercent = pageCapacity > 0
                    ? (int)((long)info.UnusedCount * 100 / pageCapacity)
                    : 0;
                bool lowUtil = pageCapacity > 0 && utilPercent < 50;
                bool longIdle = info.IdleFrames > MemoryPool.ShortDecayStartFrames;
                bool highMissRate = info.AcquireCount > 0 && info.MissRate > 0.1f;
                string colorTag = highMissRate ? "<color=#FF6347>" : (lowUtil ? "<color=#FFD700>" : (longIdle ? "<color=#FFD700>" : "<color=#FFFFFF>"));

                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label(_showFullClassName ? info.Type.FullName : info.Type.Name);
                    GUILayout.Label(info.UnusedCount.ToString(), GUILayout.Width(50f));
                    GUILayout.Label(info.UsingCount.ToString(), GUILayout.Width(50f));
                    GUILayout.Label(info.AcquireCount.ToString(), GUILayout.Width(55f));
                    GUILayout.Label(info.ReleaseCount.ToString(), GUILayout.Width(55f));
                    GUILayout.Label(colorTag + info.MissCount.ToString() + "</color>", GUILayout.Width(55f));
                    GUILayout.Label(info.TargetFreeReserve.ToString(), GUILayout.Width(55f));
                    GUILayout.Label(colorTag + info.IdleFrames.ToString() + "</color>", GUILayout.Width(50f));
                    GUILayout.Label(colorTag + pageCapacity.ToString() + "</color>", GUILayout.Width(50f));
                    GUILayout.Label(colorTag + utilPercent + "%</color>", GUILayout.Width(45f));
                }
                GUILayout.EndHorizontal();
            }

            private static int NormalClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
            {
                return a.Type.Name.CompareTo(b.Type.Name);
            }

            private static int FullClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
            {
                return a.Type.FullName.CompareTo(b.Type.FullName);
            }
        }
    }
}
