using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Moirai.Atropos.Fsm;
using Moirai.Atropos.ObjectPool;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    [CustomEditor(typeof(GameModule))]
    public class GameModuleEditor : UnityEditor.Editor
    {
        private static readonly float[] s_GameSpeed = new float[] { 0f, 0.01f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f, 8f };
        private static readonly string[] s_GameSpeedForDisplay = new string[] { "0x", "0.01x", "0.1x", "0.25x", "0.5x", "1x", "1.5x", "2x", "4x", "8x" };

        private enum StateTab { Settings, FSM, ObjectPool }
        private StateTab _currentTab = StateTab.Settings;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            GameModule t = (GameModule)target;

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Language", GameModule.Localization?.CurrentLanguage.Name);

                int frameRate = EditorGUILayout.IntSlider("Frame Rate", GameSettings.FrameRate, 1, 300);
                if (frameRate != GameSettings.FrameRate)
                {
                    GameSettings.FrameRate = frameRate;
                }

                EditorGUILayout.BeginVertical("box");
                {
                    float gameSpeed = EditorGUILayout.Slider("Game Speed", GameSettings.GameSpeed, 0f, 8f);
                    int selectedGameSpeed = GUILayout.SelectionGrid(GetSelectedGameSpeed(gameSpeed), s_GameSpeedForDisplay, 5);
                    if (selectedGameSpeed >= 0)
                    {
                        gameSpeed = GetGameSpeed(selectedGameSpeed);
                    }

                    if (Math.Abs(gameSpeed - GameSettings.GameSpeed) > 0.01f)
                    {
                        GameSettings.GameSpeed = gameSpeed;
                    }
                }
                EditorGUILayout.EndVertical();

                // 绘制模块的相关状态
                if (IsPrefabInHierarchy(t.gameObject))
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

        protected bool IsPrefabInHierarchy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return false;
            }

#if UNITY_2018_3_OR_NEWER
            return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.Regular;
#else
            return PrefabUtility.GetPrefabType(obj) != PrefabType.Prefab;
#endif
        }
    }
}