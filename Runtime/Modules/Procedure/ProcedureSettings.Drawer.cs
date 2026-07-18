#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    public sealed partial class ProcedureSettings
    {
        [NonSerialized] private string[] _procedureTypeNames;
        [NonSerialized] private List<string> _currentAvailableProcedureTypeNames;
        [NonSerialized] private int _entranceProcedureIndex = -1;
        [NonSerialized] private bool _editorInitialized;
        [NonSerialized] private bool _resetSubscribed;

        // ─────────────────────── 事件 ───────────────────────

        private void OnSettingsReset()
        {
            _editorInitialized = false;
        }

        // ─────────────────────── Odin 绘制 ───────────────────────

        [OnInspectorGUI]
        private void DrawProcedureSettings()
        {
            // 只在首次绘制时订阅一次
            if (!_resetSubscribed)
            {
                SettingsReset += OnSettingsReset;
                _resetSubscribed = true;
            }

            // 域重载或 Reset 后自动重建
            if (!_editorInitialized)
            {
                _editorInitialized = true;
                RefreshTypeNames();
            }

            // ① 入口校验 / 运行时状态
            if (string.IsNullOrEmpty(m_EntranceProcedureTypeName))
            {
                EditorGUILayout.HelpBox("Entrance procedure is invalid.", MessageType.Error);
            }
            else if (EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Current Procedure",
                    CurrentProcedure == null ? "None" : CurrentProcedure.GetType().ToString());
            }

            // ② 可用流程勾选列表
            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                GUILayout.Label("Available Procedures", EditorStyles.boldLabel);

                if (_procedureTypeNames is { Length: > 0 })
                {
                    SirenixEditorGUI.BeginBox();
                    {
                        foreach (string procedureTypeName in _procedureTypeNames)
                        {
                            bool selected = _currentAvailableProcedureTypeNames.Contains(procedureTypeName);
                            bool newSelected = EditorGUILayout.ToggleLeft(procedureTypeName, selected);

                            if (newSelected == selected) continue;

                            Undo.RecordObject(this, "Toggle Procedure");

                            if (newSelected)
                            {
                                _currentAvailableProcedureTypeNames.Add(procedureTypeName);
                            }
                            else if (procedureTypeName != m_EntranceProcedureTypeName)
                            {
                                _currentAvailableProcedureTypeNames.Remove(procedureTypeName);
                            }

                            WriteAvailableProcedureTypeNames();
                        }
                    }
                    SirenixEditorGUI.EndBox();
                }
                else
                {
                    EditorGUILayout.HelpBox("There is no available procedure.", MessageType.Warning);
                }

                // ③ 入口流程下拉
                if (_currentAvailableProcedureTypeNames.Count > 0)
                {
                    EditorGUILayout.Separator();

                    int selectedIndex = EditorGUILayout.Popup(
                        "Entrance Procedure",
                        _entranceProcedureIndex,
                        _currentAvailableProcedureTypeNames.ToArray());

                    if (selectedIndex != _entranceProcedureIndex)
                    {
                        Undo.RecordObject(this, "Change Entrance Procedure");
                        _entranceProcedureIndex = selectedIndex;
                        m_EntranceProcedureTypeName = _currentAvailableProcedureTypeNames[selectedIndex];
                        EditorUtility.SetDirty(this);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Select available procedures first.", MessageType.Info);
                }
            }
            EditorGUI.EndDisabledGroup();

            // 运行时持续刷新以更新 Current Procedure 显示
            if (EditorApplication.isPlaying)
            {
                GUIHelper.RequestRepaint();
            }
        }

        // ─────────────────────── 类型刷新 ───────────────────────

        private void RefreshTypeNames()
        {
            _procedureTypeNames = GetProcedureTypeNames();
            ReadAvailableProcedureTypeNames();

            int oldCount = _currentAvailableProcedureTypeNames.Count;
            _currentAvailableProcedureTypeNames =
                _currentAvailableProcedureTypeNames.Where(x => _procedureTypeNames.Contains(x)).ToList();

            if (_currentAvailableProcedureTypeNames.Count != oldCount)
            {
                WriteAvailableProcedureTypeNames();
            }
            else if (!string.IsNullOrEmpty(m_EntranceProcedureTypeName))
            {
                _entranceProcedureIndex = _currentAvailableProcedureTypeNames.IndexOf(m_EntranceProcedureTypeName);
                if (_entranceProcedureIndex < 0)
                {
                    m_EntranceProcedureTypeName = null;
                }
            }
        }

        private void ReadAvailableProcedureTypeNames()
        {
            _currentAvailableProcedureTypeNames = m_AvailableProcedureTypeNames != null
                ? new List<string>(m_AvailableProcedureTypeNames)
                : new List<string>();
        }

        private void WriteAvailableProcedureTypeNames()
        {
            _currentAvailableProcedureTypeNames?.Sort();
            m_AvailableProcedureTypeNames = _currentAvailableProcedureTypeNames?.ToArray();

            if (!string.IsNullOrEmpty(m_EntranceProcedureTypeName))
            {
                _entranceProcedureIndex = _currentAvailableProcedureTypeNames.IndexOf(m_EntranceProcedureTypeName);
                if (_entranceProcedureIndex < 0)
                {
                    m_EntranceProcedureTypeName = null;
                }
            }

            EditorUtility.SetDirty(this);
        }
    }
}
#endif