using System.Collections.Generic;
using System.Linq;
using Moirai.Atropos.Editor.Inspector;
using Moirai.Atropos.Procedure;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.GameSettings
{
    [CustomEditor(typeof(ProcedureSettings))]
    // ReSharper disable once InconsistentNaming
    internal sealed class ProcedureSettingsEditor : GameFrameworkInspector
    {
        private SerializedProperty _availableProcedureTypeNames = null;
        private SerializedProperty _entranceProcedureTypeName = null;

        private string[] _procedureTypeNames = null;
        private List<string> _currentAvailableProcedureTypeNames = null;
        private int _entranceProcedureIndex = -1;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            ProcedureSettings t = (ProcedureSettings)target;

            if (string.IsNullOrEmpty(_entranceProcedureTypeName.stringValue))
            {
                EditorGUILayout.HelpBox("Entrance procedure is invalid.", MessageType.Error);
            }
            else if (EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Current Procedure", ProcedureSettings.CurrentProcedure == null ? "None" : ProcedureSettings.CurrentProcedure.GetType().ToString());
            }

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                GUILayout.Label("Available Procedures", EditorStyles.boldLabel);
                if (_procedureTypeNames.Length > 0)
                {
                    EditorGUILayout.BeginVertical("box");
                    {
                        foreach (string procedureTypeName in _procedureTypeNames)
                        {
                            bool selected = _currentAvailableProcedureTypeNames.Contains(procedureTypeName);
                            if (selected != EditorGUILayout.ToggleLeft(procedureTypeName, selected))
                            {
                                if (!selected)
                                {
                                    _currentAvailableProcedureTypeNames.Add(procedureTypeName);
                                    WriteAvailableProcedureTypeNames();
                                }
                                else if (procedureTypeName != _entranceProcedureTypeName.stringValue)
                                {
                                    _currentAvailableProcedureTypeNames.Remove(procedureTypeName);
                                    WriteAvailableProcedureTypeNames();
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox("There is no available procedure.", MessageType.Warning);
                }

                if (_currentAvailableProcedureTypeNames.Count > 0)
                {
                    EditorGUILayout.Separator();

                    int selectedIndex = EditorGUILayout.Popup("Entrance Procedure", _entranceProcedureIndex, _currentAvailableProcedureTypeNames.ToArray());
                    if (selectedIndex != _entranceProcedureIndex)
                    {
                        _entranceProcedureIndex = selectedIndex;
                        _entranceProcedureTypeName.stringValue = _currentAvailableProcedureTypeNames[selectedIndex];
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Select available procedures first.", MessageType.Info);
                }
            }
            EditorGUI.EndDisabledGroup();

            serializedObject.ApplyModifiedProperties();

            Repaint();
        }

        protected override void OnCompileComplete()
        {
            base.OnCompileComplete();

            RefreshTypeNames();
        }

        private void OnEnable()
        {
            _availableProcedureTypeNames = serializedObject.FindProperty("m_AvailableProcedureTypeNames");
            _entranceProcedureTypeName = serializedObject.FindProperty("m_EntranceProcedureTypeName");

            RefreshTypeNames();
        }

        private void RefreshTypeNames()
        {
            _procedureTypeNames = TypeUtility.GetRuntimeTypeNames(typeof(ProcedureBase));
            ReadAvailableProcedureTypeNames();
            int oldCount = _currentAvailableProcedureTypeNames.Count;
            _currentAvailableProcedureTypeNames = _currentAvailableProcedureTypeNames.Where(x => _procedureTypeNames.Contains(x)).ToList();
            if (_currentAvailableProcedureTypeNames.Count != oldCount)
            {
                WriteAvailableProcedureTypeNames();
            }
            else if (!string.IsNullOrEmpty(_entranceProcedureTypeName.stringValue))
            {
                _entranceProcedureIndex = _currentAvailableProcedureTypeNames.IndexOf(_entranceProcedureTypeName.stringValue);
                if (_entranceProcedureIndex < 0)
                {
                    _entranceProcedureTypeName.stringValue = null;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ReadAvailableProcedureTypeNames()
        {
            _currentAvailableProcedureTypeNames = new List<string>();
            int count = _availableProcedureTypeNames.arraySize;
            for (int i = 0; i < count; i++)
            {
                _currentAvailableProcedureTypeNames.Add(_availableProcedureTypeNames.GetArrayElementAtIndex(i).stringValue);
            }
        }

        private void WriteAvailableProcedureTypeNames()
        {
            _availableProcedureTypeNames.ClearArray();
            if (_currentAvailableProcedureTypeNames == null)
            {
                return;
            }

            _currentAvailableProcedureTypeNames.Sort();
            int count = _currentAvailableProcedureTypeNames.Count;
            for (int i = 0; i < count; i++)
            {
                _availableProcedureTypeNames.InsertArrayElementAtIndex(i);
                _availableProcedureTypeNames.GetArrayElementAtIndex(i).stringValue = _currentAvailableProcedureTypeNames[i];
            }

            if (!string.IsNullOrEmpty(_entranceProcedureTypeName.stringValue))
            {
                _entranceProcedureIndex = _currentAvailableProcedureTypeNames.IndexOf(_entranceProcedureTypeName.stringValue);
                if (_entranceProcedureIndex < 0)
                {
                    _entranceProcedureTypeName.stringValue = null;
                }
            }
        }
    }
}