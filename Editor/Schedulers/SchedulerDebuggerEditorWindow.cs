using System;
using UnityEngine;
using UnityEditor;
using System.Linq;
using Moirai.Atropos.Editor;
using Unity.CodeEditor;

namespace Moirai.Atropos.Schedulers.Editor
{
    // EditorWindow is modified from R3.Unity
    public class SchedulerDebuggerEditorWindow : EditorWindow
    {
        private static SchedulerRunner Runner => Application.isPlaying ? SchedulerRunner.Get() : null;
        private static int ManagedScheduledCount => Runner == null ? 0 : Runner.ScheduledItems.Count;

        private static int ManagedScheduledCapacity => Runner == null ? 0 : Runner.ScheduledItems.InternalCapacity;
        private static SchedulerDebuggerEditorWindow s_Window;

        [MenuItem("Window/Scheduler Debugger")]
        public static void OpenWindow()
        {
            if (s_Window != null)
            {
                s_Window.Close();
            }

            // will called OnEnable(singleton instance will be set).
            GetWindow<SchedulerDebuggerEditorWindow>("Scheduler Debugger").Show();
        }

        private static readonly GUILayoutOption[] s_EmptyLayoutOption = Array.Empty<GUILayoutOption>();

        private SchedulerDebuggerTreeView _treeView;
        private object _splitterState;

        private void OnEnable()
        {
            s_Window = this; // set singleton.
            _splitterState = SplitterGUILayout.CreateSplitterState(new float[] { 75f, 25f }, new int[] { 32, 32 }, null);
            _treeView = new SchedulerDebuggerTreeView();
        }
        private void Update()
        {
            _treeView.ReloadAndSort();
            Repaint();
        }
        private void OnGUI()
        {
            // Head
            RenderHeadPanel();

            // Splittable
            SplitterGUILayout.BeginVerticalSplit(_splitterState, s_EmptyLayoutOption);
            {
                // Column Tabble
                RenderTable();

                // StackTrace details
                RenderDetailsPanel();
            }
            SplitterGUILayout.EndVerticalSplit();
        }

        #region HeadPanel

        private static readonly GUIContent s_CancelAllHeadContent = EditorGUIUtility.TrTextContent("Cancel All", "Cancel all scheduled tasks");

        private void RenderHeadPanel()
        {
            EditorGUILayout.BeginVertical(s_EmptyLayoutOption);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, s_EmptyLayoutOption);

            GUILayout.Label($"Managed scheduled task count: {ManagedScheduledCount} capacity: {ManagedScheduledCapacity}");
            GUILayout.FlexibleSpace();

            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button(s_CancelAllHeadContent, EditorStyles.toolbarButton, s_EmptyLayoutOption))
            {
                Runner.CancelAll();
                _treeView.ReloadAndSort();
                Repaint();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region TableColumn

        private Vector2 _tableScroll;
        private GUIStyle _tableListStyle;

        private void RenderTable()
        {
            if (_tableListStyle == null)
            {
                _tableListStyle = new GUIStyle("CN Box");
                _tableListStyle.margin.top = 0;
                _tableListStyle.padding.left = 3;
            }

            EditorGUILayout.BeginVertical(_tableListStyle, s_EmptyLayoutOption);

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll, new GUILayoutOption[]
            {
                GUILayout.ExpandWidth(true),
                GUILayout.MaxWidth(2000f)
            });
            var controlRect = EditorGUILayout.GetControlRect(new GUILayoutOption[]
            {
                GUILayout.ExpandHeight(true),
                GUILayout.ExpandWidth(true)
            });


            _treeView?.OnGUI(controlRect);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }



        #endregion

        #region Details

        private static GUIStyle s_DetailsStyle;
        private static GUIStyle s_StackTraceButtonStyle;
        private Vector2 _detailsScroll;
        private void RenderDetailsPanel()
        {
            if (s_DetailsStyle == null)
            {
                s_DetailsStyle = new GUIStyle("CN Message")
                {
                    wordWrap = false,
                    stretchHeight = true
                };
                s_DetailsStyle.margin.right = 15;
            }

            s_StackTraceButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                wordWrap = true,
                fontSize = 12
            };

            SchedulerDebuggerTreeView.ViewItem viewItem = null;
            var selected = _treeView.state.selectedIDs;
            if (selected.Count > 0)
            {
                var first = selected[0];
                if (_treeView.CurrentBindingItems.FirstOrDefault(x => x.id == first) is SchedulerDebuggerTreeView.ViewItem item)
                {
                    viewItem = item;
                }
            }
            _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll, s_EmptyLayoutOption);
            if (viewItem != null)
            {
                if (SchedulerRegistry.TryGetListener(viewItem.ScheduledItem.Value, out var listener))
                {
                    GUILayout.Label($"{listener.fileName} {listener.lineNumber}", s_DetailsStyle);
                    if (GUILayout.Button($"Open in Code Editor", s_StackTraceButtonStyle))
                    {
                        CodeEditor.Editor.CurrentCodeEditor.OpenProject(listener.fileName, listener.lineNumber);
                    }
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Resume", s_StackTraceButtonStyle))
                    {
                        Runner.Resume(viewItem.ScheduledItem.Value.Handle);
                    }
                    if (GUILayout.Button("Pause", s_StackTraceButtonStyle))
                    {
                        Runner.Pause(viewItem.ScheduledItem.Value.Handle);
                    }
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button("Cancel", s_StackTraceButtonStyle))
                    {
                        Runner.Cancel(viewItem.ScheduledItem.Value.Handle);
                    }
                }
                else
                {
                    GUILayout.Label($"Enable Stack Trace in [{SchedulerDebuggerDefineSymbols.MENU_ITEM_PATH}] to track all scheduled tasks.");
                }
            }
            EditorGUILayout.EndScrollView();
        }
        #endregion
    }
}

