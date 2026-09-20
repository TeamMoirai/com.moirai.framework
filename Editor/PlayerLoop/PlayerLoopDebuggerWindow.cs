using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Moirai.Atropos.Editor.PlayerLoopDebug
{
    /// <summary>
    /// PlayerLoop 结构可视化：递归打印当前 PlayerLoop，并标注 Moirai 注入点。
    /// <para>菜单：Window → PlayerLoop Debugger</para>
    /// </summary>
    public sealed class PlayerLoopDebuggerWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _dump = string.Empty;
        private bool _autoRefresh = true;

        [MenuItem("Window/PlayerLoop Debugger")]
        public static void OpenWindow()
        {
            GetWindow<PlayerLoopDebuggerWindow>("PlayerLoop Debugger").Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnInspectorUpdate()
        {
            if (_autoRefresh && EditorApplication.isPlaying)
            {
                Refresh();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                Refresh();
            }
            if (GUILayout.Button("Ensure Injected", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                PlayerLoopInjector.EnsureInjected();
                Refresh();
            }
            if (GUILayout.Button("Reinject", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                PlayerLoopInjector.Reinject();
                Refresh();
            }
            if (GUILayout.Button("Restore Default", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                PlayerLoopInjector.RestoreDefault();
                Refresh();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Injected", PlayerLoopInjector.IsInjected ? "Yes" : "No", GUILayout.Width(120));
            EditorGUILayout.LabelField("Driver Shutdown", PlayerLoopDriver.IsShutdown ? "Yes" : "No", GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Update Handlers: {PlayerLoopDriver.UpdateHandlerCount}", GUILayout.Width(160));
            EditorGUILayout.LabelField($"Callbacks: {PlayerLoopDriver.UpdateCallbackCount}", GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Fixed Handlers: {PlayerLoopDriver.FixedUpdateHandlerCount}", GUILayout.Width(160));
            EditorGUILayout.LabelField($"Callbacks: {PlayerLoopDriver.FixedUpdateCallbackCount}", GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Late Handlers: {PlayerLoopDriver.LateUpdateHandlerCount}", GUILayout.Width(160));
            EditorGUILayout.LabelField($"Callbacks: {PlayerLoopDriver.LateUpdateCallbackCount}", GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Current PlayerLoop Structure", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_dump, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            PlayerLoopSystem loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var sb = new StringBuilder(4096);
            AppendSystem(sb, loop, 0);
            _dump = sb.ToString();
        }

        private static void AppendSystem(StringBuilder sb, PlayerLoopSystem system, int depth)
        {
            string indent = new string(' ', depth * 2);
            string typeName = system.type != null ? system.type.FullName ?? system.type.Name : "(null)";
            bool isMoirai = system.type != null &&
                            (system.type == typeof(PlayerLoopInjector.MoiraiUpdate) ||
                             system.type == typeof(PlayerLoopInjector.MoiraiFixedUpdate) ||
                             system.type == typeof(PlayerLoopInjector.MoiraiLateUpdate));

            string flag = isMoirai ? "  << Moirai" : string.Empty;
            string kind = system.updateDelegate != null ? " [delegate]" : string.Empty;
            string loopFlag = system.loopConditionFunction != null ? " [loop]" : string.Empty;

            sb.Append(indent).Append(typeName).Append(kind).Append(loopFlag).Append(flag).Append('\n');

            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
            {
                AppendSystem(sb, children[i], depth + 1);
            }
        }
    }
}
