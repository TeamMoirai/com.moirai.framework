using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Moirai.Atropos.Localization;
using Moirai.Atropos.GameObjectPool;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    [CustomEditor(typeof(GameApp))]
    public class GameAppEditor : UnityEditor.Editor
    {
        private static readonly float[] s_GameSpeed = new float[] { 0f, 0.01f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f, 8f };
        private static readonly string[] s_GameSpeedForDisplay = new string[] { "0x", "0.01x", "0.1x", "0.25x", "0.5x", "1x", "1.5x", "2x", "4x", "8x" };

        private enum StateTab { Settings, GameObjectPool }
        private StateTab _currentTab = StateTab.Settings;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            GameApp t = (GameApp)target;

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Language", GameApp.Localization?.CurrentLanguage.Name);

                int frameRate = EditorGUILayout.IntSlider("Frame Rate", GameAppSettings.FrameRate, 1, 300);
                if (frameRate != GameAppSettings.FrameRate)
                {
                    GameAppSettings.FrameRate = frameRate;
                }

                EditorGUILayout.BeginVertical("box");
                {
                    float gameSpeed = EditorGUILayout.Slider("Game Speed", GameAppSettings.GameSpeed, 0f, 8f);
                    int selectedGameSpeed = GUILayout.SelectionGrid(GetSelectedGameSpeed(gameSpeed), s_GameSpeedForDisplay, 5);
                    if (selectedGameSpeed >= 0)
                    {
                        gameSpeed = GetGameSpeed(selectedGameSpeed);
                    }

                    if (Math.Abs(gameSpeed - GameAppSettings.GameSpeed) > 0.01f)
                    {
                        GameAppSettings.GameSpeed = gameSpeed;
                    }
                }
                EditorGUILayout.EndVertical();

                // 绘制服务的相关状态
                if (IsPrefabInHierarchy(t.gameObject))
                {
                    EditorGUILayout.Space(10);
                    _currentTab = (StateTab)GUILayout.Toolbar(
                        (int)_currentTab,
                        new[] { "Settings", "Object Pool" },
                        GUILayout.Height(25));

                    EditorGUILayout.BeginVertical("box");
                    {
                        switch (_currentTab)
                        {
                            case StateTab.Settings:
                                DrawSettingState();
                                break;
                            case StateTab.GameObjectPool:
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

        private readonly HashSet<string> _mOpenedItems = new HashSet<string>();
        private void DrawObjectPoolState()
        {
            var gameObjectPoolService = GameApp.GameObjectPool;
            if (!(gameObjectPoolService is GameObjectPoolService service))
            {
                EditorGUILayout.LabelField("Service does not support debug interface.");
                return;
            }

            var summary = service.GetDebugSummary();
            EditorGUILayout.LabelField("Pool Count", summary.PoolCount.ToString());
            EditorGUILayout.LabelField("Loaded Prefab Count", summary.LoadedPrefabCount.ToString());
            EditorGUILayout.LabelField("Total Instance Count", summary.TotalInstanceCount.ToString());
            EditorGUILayout.LabelField("Active Instance Count", summary.ActiveInstanceCount.ToString());
            EditorGUILayout.LabelField("Inactive Instance Count", summary.InactiveInstanceCount.ToString());

            GameObjectPoolSnapshot[] snapshots = new GameObjectPoolSnapshot[64];
            int count = service.GetDebugSnapshots(snapshots);
            for (int i = 0; i < count; i++)
            {
                GameObjectPoolSnapshot snapshot = snapshots[i];
                bool lastState = _mOpenedItems.Contains(snapshot.location);
                bool currentState = EditorGUILayout.Foldout(lastState, snapshot.location);
                if (currentState != lastState)
                {
                    if (currentState)
                    {
                        _mOpenedItems.Add(snapshot.location);
                    }
                    else
                    {
                        _mOpenedItems.Remove(snapshot.location);
                    }
                }

                if (currentState)
                {
                    EditorGUILayout.BeginVertical("box");
                    {
                        EditorGUILayout.LabelField("Entry Name", snapshot.entryName);
                        EditorGUILayout.LabelField("Group", snapshot.group);
                        EditorGUILayout.LabelField("Policy", snapshot.policy.ToString());
                        EditorGUILayout.LabelField("Min Idle", snapshot.minIdle.ToString());
                        EditorGUILayout.LabelField("Soft Capacity", snapshot.softCapacity.ToString());
                        EditorGUILayout.LabelField("Hard Capacity", snapshot.hardCapacity.ToString());
                        EditorGUILayout.LabelField("Total Count", snapshot.totalCount.ToString());
                        EditorGUILayout.LabelField("Active Count", snapshot.activeCount.ToString());
                        EditorGUILayout.LabelField("Inactive Count", snapshot.inactiveCount.ToString());
                        EditorGUILayout.LabelField("Prefab Loaded", snapshot.prefabLoaded.ToString());
                        EditorGUILayout.LabelField("Spawn/Despawn", StringUtility.Format("{0}/{1}", snapshot.spawnCount, snapshot.despawnCount));
                        EditorGUILayout.LabelField("Hit/Miss", StringUtility.Format("{0}/{1}", snapshot.hitCount, snapshot.missCount));
                        EditorGUILayout.LabelField("Expand/Destroy", StringUtility.Format("{0}/{1}", snapshot.expandCount, snapshot.destroyCount));
                        EditorGUILayout.LabelField("Peak Active", snapshot.peakActive.ToString());
                    }
                    EditorGUILayout.EndVertical();
                }

                MemoryPool.Release(snapshot);
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