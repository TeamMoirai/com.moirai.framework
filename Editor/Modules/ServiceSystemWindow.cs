using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public sealed class ServiceSystemWindow : EditorWindow
    {
        [MenuItem("Window/Moirai/Service System", false, 100)]
        private static void Open()
        {
            GetWindow<ServiceSystemWindow>(false, "Service System", true);
        }

        private Vector2 _scrollPosition;
        private bool _showRuntimeState = true;

        private void OnGUI()
        {
            titleContent = new GUIContent("Service System");

            EditorGUILayout.Space(4);

            DrawSummary();

            EditorGUILayout.Space(6);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawServiceList();
            EditorGUILayout.EndScrollView();

            if (GUI.changed)
            {
                Repaint();
            }
        }

        private void DrawSummary()
        {
            var infos = GameServices.GetDiagnosticInfo();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Service System", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            int appCount = 0, sceneCount = 0;
            int updateCount = 0, fixedUpdateCount = 0, lateUpdateCount = 0, gizmoCount = 0;

            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                if (info.Scope == EServiceScopeKind.App) appCount++;
                else sceneCount++;
                if (info.HasUpdate) updateCount++;
                if (info.HasFixedUpdate) fixedUpdateCount++;
                if (info.HasLateUpdate) lateUpdateCount++;
                if (info.HasGizmo) gizmoCount++;
            }

            EditorGUILayout.LabelField("Registered Services", infos.Count.ToString());
            EditorGUILayout.LabelField("  App Scope", appCount.ToString());
            EditorGUILayout.LabelField("  Scene Scope", sceneCount.ToString());
            EditorGUILayout.LabelField("  Update", updateCount.ToString());
            EditorGUILayout.LabelField("  FixedUpdate", fixedUpdateCount.ToString());
            EditorGUILayout.LabelField("  LateUpdate", lateUpdateCount.ToString());
            EditorGUILayout.LabelField("  Gizmo", gizmoCount.ToString());

            EditorGUILayout.Space(4);

            _showRuntimeState = EditorGUILayout.Foldout(_showRuntimeState, "Runtime State");
            if (_showRuntimeState)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("App Container", GameServices.AppContainer != null ? "Active" : "—");
                EditorGUILayout.LabelField("Scene Container", GameServices.SceneContainer != null ? "Active" : "—");
                EditorGUILayout.LabelField("Gameplay Container", GameServices.GameplayContainer != null ? "Active" : "—");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawServiceList()
        {
            var infos = GameServices.GetDiagnosticInfo();

            EditorGUILayout.BeginVertical();
            {
                // Header
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField("#", GUILayout.Width(30));
                EditorGUILayout.LabelField("Interface", GUILayout.Width(260));
                EditorGUILayout.LabelField("Implementation", GUILayout.Width(260));
                EditorGUILayout.LabelField("Scope", GUILayout.Width(60));
                EditorGUILayout.LabelField("Priority", GUILayout.Width(40));
                EditorGUILayout.LabelField("Tick Flags", GUILayout.MinWidth(100));
                EditorGUILayout.EndHorizontal();

                for (int i = 0; i < infos.Count; i++)
                {
                    DrawServiceRow(i, infos[i]);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawServiceRow(int index, GameServices.DiagnosticInfo info)
        {
            EditorGUILayout.BeginHorizontal(index % 2 == 0 ? "box" : "HelpBox");
            {
                EditorGUILayout.LabelField(index.ToString(), GUILayout.Width(30));
                EditorGUILayout.LabelField(info.InterfaceType ?? "<unknown>", GUILayout.Width(260));
                EditorGUILayout.LabelField(info.ImplementationType ?? "<unknown>", GUILayout.Width(260));

                var scopeStyle = info.Scope == EServiceScopeKind.Scene
                    ? new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.6f, 0.2f) } }
                    : EditorStyles.label;
                EditorGUILayout.LabelField(info.Scope.ToString(), scopeStyle, GUILayout.Width(60));

                EditorGUILayout.LabelField(info.Priority.ToString(), GUILayout.Width(40));

                // Tick flags
                var flags = new List<string>(4);
                if (info.HasUpdate) flags.Add("U");
                if (info.HasFixedUpdate) flags.Add("F");
                if (info.HasLateUpdate) flags.Add("L");
                if (info.HasGizmo) flags.Add("G");
                EditorGUILayout.LabelField(flags.Count > 0 ? string.Join(" ", flags) : "—", GUILayout.MinWidth(100));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                Repaint();
            }
        }
    }
}
