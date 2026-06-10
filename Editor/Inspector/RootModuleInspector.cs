using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Moirai.Atropos.Editor.Inspector;
using Moirai.Atropos.Fsm;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    [CustomEditor(typeof(RootModule))]
    internal sealed class RootModuleInspector : GameFrameworkInspector
    {
        private const string NONE_OPTION_NAME = "<None>";
        private static readonly float[] s_GameSpeed = new float[] { 0f, 0.01f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f, 8f };
        private static readonly string[] s_GameSpeedForDisplay = new string[] { "0x", "0.01x", "0.1x", "0.25x", "0.5x", "1x", "1.5x", "2x", "4x", "8x" };
        private static readonly string[] s_LanguageOptions = Language.BuiltinLanguages.Select(lang => lang.ToString()).ToArray();

        private SerializedProperty _editorLanguage = null;
        private SerializedProperty _formatHelperTypeName = null;
        private SerializedProperty _versionHelperTypeName = null;
        private SerializedProperty _logHelperTypeName = null;
        private SerializedProperty _compressionHelperTypeName = null;
        private SerializedProperty _objectHelperTypeName = null;
        private SerializedProperty _jsonHelperTypeName = null;
        private SerializedProperty _frameRate = null;
        private SerializedProperty _gameSpeed = null;
        private SerializedProperty _runInBackground = null;
        private SerializedProperty _neverSleep = null;

        private int _editorLanguageIndex = 0;
        private string[] _formatHelperTypeNames = null;
        private int _formatHelperTypeNameIndex = 0;
        private string[] _versionHelperTypeNames = null;
        private int _versionHelperTypeNameIndex = 0;
        private string[] _logHelperTypeNames = null;
        private int _logHelperTypeNameIndex = 0;
        private string[] _objectHelperTypeNames = null;
        private int _objectHelperTypeNameIndex = 0;
        private string[] _jsonHelperTypeNames = null;
        private int _jsonHelperTypeNameIndex = 0;

        private enum StateTab { Settings, FSM, ObjectPool }
        private StateTab _currentTab = StateTab.Settings;
        
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            RootModule t = (RootModule)target;

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                // EditorGUILayout.PropertyField(m_EditorLanguage);
                int languageSelectedIndex =  EditorGUILayout.Popup("Editor Language", _editorLanguageIndex, s_LanguageOptions);
                if (languageSelectedIndex != _editorLanguageIndex)
                {
                    _editorLanguageIndex = languageSelectedIndex;
                    _editorLanguage.stringValue = languageSelectedIndex < 0 ? null : s_LanguageOptions[languageSelectedIndex];
                }

                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.LabelField("Global Helpers", EditorStyles.boldLabel);

                    int formatHelperSelectedIndex = EditorGUILayout.Popup("Format Helper", _formatHelperTypeNameIndex, _formatHelperTypeNames);
                    if (formatHelperSelectedIndex != _formatHelperTypeNameIndex)
                    {
                        _formatHelperTypeNameIndex = formatHelperSelectedIndex;
                        _formatHelperTypeName.stringValue = formatHelperSelectedIndex <= 0 ? null : _formatHelperTypeNames[formatHelperSelectedIndex];
                    }

                    int versionHelperSelectedIndex = EditorGUILayout.Popup("Version Helper", _versionHelperTypeNameIndex, _versionHelperTypeNames);
                    if (versionHelperSelectedIndex != _versionHelperTypeNameIndex)
                    {
                        _versionHelperTypeNameIndex = versionHelperSelectedIndex;
                        _versionHelperTypeName.stringValue = versionHelperSelectedIndex <= 0 ? null : _versionHelperTypeNames[versionHelperSelectedIndex];
                    }

                    int logHelperSelectedIndex = EditorGUILayout.Popup("Log Helper", _logHelperTypeNameIndex, _logHelperTypeNames);
                    if (logHelperSelectedIndex != _logHelperTypeNameIndex)
                    {
                        _logHelperTypeNameIndex = logHelperSelectedIndex;
                        _logHelperTypeName.stringValue = logHelperSelectedIndex <= 0 ? null : _logHelperTypeNames[logHelperSelectedIndex];
                    }
                    
                    int objectHelperSelectedIndex = EditorGUILayout.Popup("Object Helper", _objectHelperTypeNameIndex, _objectHelperTypeNames);
                    if (objectHelperSelectedIndex != _objectHelperTypeNameIndex)
                    {
                        _objectHelperTypeNameIndex = objectHelperSelectedIndex;
                        _objectHelperTypeName.stringValue = objectHelperSelectedIndex <= 0 ? null : _objectHelperTypeNames[objectHelperSelectedIndex];
                    }
                    
                    int jsonHelperSelectedIndex = EditorGUILayout.Popup("JSON Helper", _jsonHelperTypeNameIndex, _jsonHelperTypeNames);
                    if (jsonHelperSelectedIndex != _jsonHelperTypeNameIndex)
                    {
                        _jsonHelperTypeNameIndex = jsonHelperSelectedIndex;
                        _jsonHelperTypeName.stringValue = jsonHelperSelectedIndex <= 0 ? null : _jsonHelperTypeNames[jsonHelperSelectedIndex];
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUI.EndDisabledGroup();

            int frameRate = EditorGUILayout.IntSlider("Frame Rate", _frameRate.intValue, 1, 300);
            if (frameRate != _frameRate.intValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.FrameRate = frameRate;
                }
                else
                {
                    _frameRate.intValue = frameRate;
                }
            }

            EditorGUILayout.BeginVertical("box");
            {
                float gameSpeed = EditorGUILayout.Slider("Game Speed", _gameSpeed.floatValue, 0f, 8f);
                int selectedGameSpeed = GUILayout.SelectionGrid(GetSelectedGameSpeed(gameSpeed), s_GameSpeedForDisplay, 5);
                if (selectedGameSpeed >= 0)
                {
                    gameSpeed = GetGameSpeed(selectedGameSpeed);
                }

                if (Math.Abs(gameSpeed - _gameSpeed.floatValue) > 0.01f)
                {
                    if (EditorApplication.isPlaying)
                    {
                        t.GameSpeed = gameSpeed;
                    }
                    else
                    {
                        _gameSpeed.floatValue = gameSpeed;
                    }
                }
            }
            EditorGUILayout.EndVertical();

            bool runInBackground = EditorGUILayout.Toggle("Run in Background", _runInBackground.boolValue);
            if (runInBackground != _runInBackground.boolValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.RunInBackground = runInBackground;
                }
                else
                {
                    _runInBackground.boolValue = runInBackground;
                }
            }

            bool neverSleep = EditorGUILayout.Toggle("Never Sleep", _neverSleep.boolValue);
            if (neverSleep != _neverSleep.boolValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.NeverSleep = neverSleep;
                }
                else
                {
                    _neverSleep.boolValue = neverSleep;
                }
            }
            
            // 绘制模块的相关状态
            if (EditorApplication.isPlaying && IsPrefabInHierarchy(t.gameObject))
            {
                EditorGUILayout.Space(10);
                _currentTab = (StateTab)GUILayout.Toolbar(
                    (int)_currentTab, 
                    new[] { "Settings", "FSM", "Object Pool" },
                    GUILayout.Height(25));

                EditorGUILayout.BeginVertical("box");
                {
                    switch (_currentTab)
                    {
                        case StateTab.Settings:
                            DrawSettingState();
                            break;
                        case StateTab.FSM:
                            DrawFsmState();
                            break;
                        case StateTab.ObjectPool:
                            DrawObjectPoolState();
                            break;
                    }
                }
                EditorGUILayout.EndVertical();
                
                Repaint();
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawSettingState()
        {
            EditorGUILayout.LabelField("Setting Count", SettingUtility.Count >= 0 ? SettingUtility.Count.ToString() : "<Unknown>");
            if (SettingUtility.Count > 0)
            {
                string[] settingNames = SettingUtility.GetAllSettingNames();
                foreach (string settingName in settingNames)
                {
                    EditorGUILayout.LabelField(settingName, SettingUtility.GetString(settingName));
                }
            }
                
            if (GUILayout.Button("Save Settings"))
            {
                SettingUtility.Save();
            }
            if (GUILayout.Button("Remove All Settings"))
            {
                SettingUtility.RemoveAllSettings();
            }
        }

        private void DrawFsmState()
        {
            var fsmModule = GameModule.Fsm;
            EditorGUILayout.LabelField("FSM Count", fsmModule.Count.ToString());

            FsmBase[] fsms = fsmModule.GetAllFsms();
            foreach (FsmBase fsm in fsms)
            {
                EditorGUILayout.LabelField(fsm.Name,
                    fsm.IsRunning ? TextUtility.Format("{0}, {1:F1} s", fsm.CurrentStateName, fsm.CurrentStateTime) : (fsm.IsDestroyed ? "Destroyed" : "Not Running"));
            }
        }

        private readonly HashSet<string> _mOpenedItems = new HashSet<string>();
        private void DrawObjectPoolState()
        {
            var objectPoolModule = GameModule.ObjectPool;
            EditorGUILayout.LabelField("Object Pool Count", objectPoolModule.Count.ToString());

            ObjectPoolBase[] objectPools = objectPoolModule.GetAllObjectPools(true);
            foreach (ObjectPoolBase objectPool in objectPools)
            {
                bool lastState = _mOpenedItems.Contains(objectPool.FullName);
                bool currentState = EditorGUILayout.Foldout(lastState, objectPool.Name);
                if (currentState != lastState)
                {
                    if (currentState)
                    {
                        _mOpenedItems.Add(objectPool.FullName);
                    }
                    else
                    {
                        _mOpenedItems.Remove(objectPool.FullName);
                    }
                }

                if (currentState)
                {
                    EditorGUILayout.BeginVertical("box");
                    {
                        EditorGUILayout.LabelField("Name", objectPool.Name);
                        EditorGUILayout.LabelField("Type", objectPool.ObjectType.FullName);
                        EditorGUILayout.LabelField("Auto Release Interval", objectPool.AutoReleaseInterval.ToString());
                        EditorGUILayout.LabelField("Capacity", objectPool.Capacity.ToString());
                        EditorGUILayout.LabelField("Used Count", objectPool.Count.ToString());
                        EditorGUILayout.LabelField("Can Release Count", objectPool.CanReleaseCount.ToString());
                        EditorGUILayout.LabelField("Expire Time", objectPool.ExpireTime.ToString());
                        EditorGUILayout.LabelField("Priority", objectPool.Priority.ToString());
                        ObjectInfo[] objectInfos = objectPool.GetAllObjectInfos();
                        if (objectInfos.Length > 0)
                        {
                            EditorGUILayout.LabelField("Name",
                                objectPool.AllowMultiSpawn ? "Locked\tCount\tFlag\tPriority\tLast Use Time" : "Locked\tIn Use\tFlag\tPriority\tLast Use Time");
                            foreach (ObjectInfo objectInfo in objectInfos)
                            {
                                EditorGUILayout.LabelField(string.IsNullOrEmpty(objectInfo.Name) ? "<None>" : objectInfo.Name,
                                    objectPool.AllowMultiSpawn
                                        ? TextUtility.Format("{0}\t{1}\t{2}\t{3}\t{4:yyyy-MM-dd HH:mm:ss}", objectInfo.Locked, objectInfo.SpawnCount,
                                            objectInfo.CustomCanReleaseFlag,
                                            objectInfo.Priority, objectInfo.LastUseTime.ToLocalTime())
                                        : TextUtility.Format("{0}\t{1}\t{2}\t{3}\t{4:yyyy-MM-dd HH:mm:ss}", objectInfo.Locked, objectInfo.IsInUse,
                                            objectInfo.CustomCanReleaseFlag,
                                            objectInfo.Priority, objectInfo.LastUseTime.ToLocalTime()));
                            }

                            if (GUILayout.Button("Release"))
                            {
                                objectPool.Release();
                            }

                            if (GUILayout.Button("Release All Unused"))
                            {
                                objectPool.ReleaseAllUnused();
                            }

                            if (GUILayout.Button("Export CSV Data"))
                            {
                                string exportFileName = EditorUtility.SaveFilePanel("Export CSV Data", string.Empty,
                                    TextUtility.Format("Object Pool Data - {0}.csv", objectPool.Name),
                                    string.Empty);
                                if (!string.IsNullOrEmpty(exportFileName))
                                {
                                    try
                                    {
                                        int index = 0;
                                        string[] data = new string[objectInfos.Length + 1];
                                        data[index++] = TextUtility.Format("Name,Locked,{0},Custom Can Release Flag,Priority,Last Use Time",
                                            objectPool.AllowMultiSpawn ? "Count" : "In Use");
                                        foreach (ObjectInfo objectInfo in objectInfos)
                                        {
                                            data[index++] = objectPool.AllowMultiSpawn
                                                ? TextUtility.Format("{0},{1},{2},{3},{4},{5:yyyy-MM-dd HH:mm:ss}", objectInfo.Name, objectInfo.Locked,
                                                    objectInfo.SpawnCount,
                                                    objectInfo.CustomCanReleaseFlag, objectInfo.Priority, objectInfo.LastUseTime.ToLocalTime())
                                                : TextUtility.Format("{0},{1},{2},{3},{4},{5:yyyy-MM-dd HH:mm:ss}", objectInfo.Name, objectInfo.Locked,
                                                    objectInfo.IsInUse,
                                                    objectInfo.CustomCanReleaseFlag, objectInfo.Priority, objectInfo.LastUseTime.ToLocalTime());
                                        }

                                        File.WriteAllLines(exportFileName, data, Encoding.UTF8);
                                        Debug.Log(TextUtility.Format("Export object pool CSV data to '{0}' success.", exportFileName));
                                    }
                                    catch (Exception exception)
                                    {
                                        Debug.LogError(TextUtility.Format("Export object pool CSV data to '{0}' failure, exception is '{1}'.", exportFileName,
                                            exception));
                                    }
                                }
                            }
                        }
                        else
                        {
                            GUILayout.Label("Object Pool is Empty ...");
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Separator();
                }
            }
        }

        protected override void OnCompileComplete()
        {
            base.OnCompileComplete();

            RefreshTypeNames();
        }

        private void OnEnable()
        {
            _editorLanguage = serializedObject.FindProperty("m_EditorLanguage");
            _formatHelperTypeName = serializedObject.FindProperty("m_FormatHelperTypeName");
            _versionHelperTypeName = serializedObject.FindProperty("m_VersionHelperTypeName");
            _logHelperTypeName = serializedObject.FindProperty("m_LogHelperTypeName");
            _compressionHelperTypeName = serializedObject.FindProperty("m_CompressionHelperTypeName");
            _objectHelperTypeName = serializedObject.FindProperty("m_ObjectHelperTypeName");
            _jsonHelperTypeName = serializedObject.FindProperty("m_JsonHelperTypeName");
            _frameRate = serializedObject.FindProperty("m_FrameRate");
            _gameSpeed = serializedObject.FindProperty("m_GameSpeed");
            _runInBackground = serializedObject.FindProperty("m_RunInBackground");
            _neverSleep = serializedObject.FindProperty("m_NeverSleep");

            RefreshLanguageOptions();
            RefreshTypeNames();
        }

        private void RefreshLanguageOptions()
        {
            _editorLanguageIndex = 0;
            if (!string.IsNullOrEmpty(_editorLanguage.stringValue))
            {
                _editorLanguageIndex = Array.IndexOf(s_LanguageOptions, _editorLanguage.stringValue);
                if (_editorLanguageIndex <= 0)
                {
                    _editorLanguageIndex = 0;
                    _editorLanguage.stringValue = s_LanguageOptions[0];
                }
            }
        }

        private void RefreshTypeNames()
        {
            #region Format Helper

            List<string> formatHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            formatHelperTypeNames.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(TextUtility.IFormatHelper)));
            _formatHelperTypeNames = formatHelperTypeNames.ToArray();
            _formatHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_formatHelperTypeName.stringValue))
            {
                _formatHelperTypeNameIndex = formatHelperTypeNames.IndexOf(_formatHelperTypeName.stringValue);
                if (_formatHelperTypeNameIndex <= 0)
                {
                    _formatHelperTypeNameIndex = 0;
                    _formatHelperTypeName.stringValue = null;
                }
            }

            #endregion
            
            #region Version Helper

            List<string> versionHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            versionHelperTypeNames.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(VersionUtility.IVersionHelper)));
            _versionHelperTypeNames = versionHelperTypeNames.ToArray();
            _versionHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_versionHelperTypeName.stringValue))
            {
                _versionHelperTypeNameIndex = versionHelperTypeNames.IndexOf(_versionHelperTypeName.stringValue);
                if (_versionHelperTypeNameIndex <= 0)
                {
                    _versionHelperTypeNameIndex = 0;
                    _versionHelperTypeName.stringValue = null;
                }
            }

            #endregion
            
            #region Log Helper

            List<string> logHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            logHelperTypeNames.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(LogUtility.ILogHelper)));
            _logHelperTypeNames = logHelperTypeNames.ToArray();
            _logHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_logHelperTypeName.stringValue))
            {
                _logHelperTypeNameIndex = logHelperTypeNames.IndexOf(_logHelperTypeName.stringValue);
                if (_logHelperTypeNameIndex <= 0)
                {
                    _logHelperTypeNameIndex = 0;
                    _logHelperTypeName.stringValue = null;
                }
            }

            #endregion
            
            #region Object Helper

            List<string> objectHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            objectHelperTypeNames.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(ObjectUtility.IObjectHelper)));
            _objectHelperTypeNames = objectHelperTypeNames.ToArray();
            _objectHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_objectHelperTypeName.stringValue))
            {
                _objectHelperTypeNameIndex = objectHelperTypeNames.IndexOf(_objectHelperTypeName.stringValue);
                if (_objectHelperTypeNameIndex <= 0)
                {
                    _objectHelperTypeNameIndex = 0;
                    _objectHelperTypeName.stringValue = null;
                }
            }

            #endregion
            
            #region Json Helper

            List<string> jsonHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            jsonHelperTypeNames.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(JSONUtility.IJsonHelper)));
            _jsonHelperTypeNames = jsonHelperTypeNames.ToArray();
            _jsonHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_jsonHelperTypeName.stringValue))
            {
                _jsonHelperTypeNameIndex = jsonHelperTypeNames.IndexOf(_jsonHelperTypeName.stringValue);
                if (_jsonHelperTypeNameIndex <= 0)
                {
                    _jsonHelperTypeNameIndex = 0;
                    _jsonHelperTypeName.stringValue = null;
                }
            }

            #endregion
            
            serializedObject.ApplyModifiedProperties();
        }

        private float GetGameSpeed(int selectedGameSpeed)
        {
            if (selectedGameSpeed < 0)
            {
                return s_GameSpeed[0];
            }

            if (selectedGameSpeed >= s_GameSpeed.Length)
            {
                return s_GameSpeed[s_GameSpeed.Length - 1];
            }

            return s_GameSpeed[selectedGameSpeed];
        }

        private int GetSelectedGameSpeed(float gameSpeed)
        {
            for (int i = 0; i < s_GameSpeed.Length; i++)
            {
                if (gameSpeed == s_GameSpeed[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
