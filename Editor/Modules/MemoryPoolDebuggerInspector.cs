using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.Inspector
{
    [CustomEditor(typeof(MemoryPoolDebugger))]
    internal sealed class MemoryPoolDebuggerInspector : GameFrameworkInspector
    {
        private readonly Dictionary<string, List<MemoryPoolInfo>> _memoryPoolInfos = new Dictionary<string, List<MemoryPoolInfo>>(StringComparer.Ordinal);
        private readonly HashSet<string> _openedItems = new HashSet<string>();

        private bool _showFullClassName = false;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            MemoryPoolDebugger t = (MemoryPoolDebugger)target;

            if (EditorApplication.isPlaying && IsPrefabInHierarchy(t.gameObject))
            {
                EditorGUILayout.LabelField("Memory Pool Count", MemoryPool.Count.ToString());
                _showFullClassName = EditorGUILayout.Toggle("Show Full Class Name", _showFullClassName);
                _memoryPoolInfos.Clear();
                MemoryPoolInfo[] memoryPoolInfos = MemoryPool.GetAllMemoryPoolInfos();
                foreach (MemoryPoolInfo memoryPoolInfo in memoryPoolInfos)
                {
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
                    bool lastState = _openedItems.Contains(assemblyMemoryPoolInfo.Key);
                    bool currentState = EditorGUILayout.Foldout(lastState, assemblyMemoryPoolInfo.Key);
                    if (currentState != lastState)
                    {
                        if (currentState)
                        {
                            _openedItems.Add(assemblyMemoryPoolInfo.Key);
                        }
                        else
                        {
                            _openedItems.Remove(assemblyMemoryPoolInfo.Key);
                        }
                    }

                    if (currentState)
                    {
                        EditorGUILayout.BeginVertical("box");
                        {
                            EditorGUILayout.LabelField(_showFullClassName ? "Full Class Name" : "Class Name", "Unused\tUsing\tAcquire\tRelease\tAdd\tRemove");
                            assemblyMemoryPoolInfo.Value.Sort(Comparison);
                            foreach (MemoryPoolInfo memoryPoolInfo in assemblyMemoryPoolInfo.Value)
                            {
                                DrawMemoryPoolInfo(memoryPoolInfo);
                            }

                            if (GUILayout.Button("Export CSV Data"))
                            {
                                string exportFileName = EditorUtility.SaveFilePanel("Export CSV Data", string.Empty, StringUtility.Format("Memory Pool Data - {0}.csv", assemblyMemoryPoolInfo.Key), string.Empty);
                                if (!string.IsNullOrEmpty(exportFileName))
                                {
                                    try
                                    {
                                        int index = 0;
                                        string[] data = new string[assemblyMemoryPoolInfo.Value.Count + 1];
                                        data[index++] = "Class Name,Full Class Name,Unused,Using,Acquire,Release,Add,Remove";
                                        foreach (MemoryPoolInfo memoryPoolInfo in assemblyMemoryPoolInfo.Value)
                                        {
                                            data[index++] = StringUtility.Format("{0},{1},{2},{3},{4},{5},{6},{7}", memoryPoolInfo.Type.Name, memoryPoolInfo.Type.FullName, memoryPoolInfo.UnusedMemoryCount.ToString(), memoryPoolInfo.UsingMemoryCount.ToString(), memoryPoolInfo.AcquireMemoryCount.ToString(), memoryPoolInfo.ReleaseMemoryCount.ToString(), memoryPoolInfo.AddMemoryCount.ToString(), memoryPoolInfo.RemoveMemoryCount.ToString());
                                        }

                                        File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                                        Debug.Log(StringUtility.Format("Export memory pool CSV data to '{0}' success.", exportFileName));
                                    }
                                    catch (Exception exception)
                                    {
                                        Debug.LogError(StringUtility.Format("Export memory pool CSV data to '{0}' failure, exception is '{1}'.", exportFileName, exception.ToString()));
                                    }
                                }
                            }
                        }
                        EditorGUILayout.EndVertical();

                        EditorGUILayout.Separator();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();

            Repaint();
        }

        private void DrawMemoryPoolInfo(MemoryPoolInfo memoryPoolInfo)
        {
            EditorGUILayout.LabelField(_showFullClassName ? memoryPoolInfo.Type.FullName : memoryPoolInfo.Type.Name, StringUtility.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", memoryPoolInfo.UnusedMemoryCount.ToString(), memoryPoolInfo.UsingMemoryCount.ToString(), memoryPoolInfo.AcquireMemoryCount.ToString(), memoryPoolInfo.ReleaseMemoryCount.ToString(), memoryPoolInfo.AddMemoryCount.ToString(), memoryPoolInfo.RemoveMemoryCount.ToString()));
        }

        private int Comparison(MemoryPoolInfo a, MemoryPoolInfo b)
        {
            if (_showFullClassName)
            {
                return a.Type.FullName.CompareTo(b.Type.FullName);
            }
            else
            {
                return a.Type.Name.CompareTo(b.Type.Name);
            }
        }
    }
}