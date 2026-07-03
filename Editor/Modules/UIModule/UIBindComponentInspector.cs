using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    [CustomEditor(typeof(UIBindComponent), true)]
    public class UIBindComponentInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawTopButtons();
            EditorGUILayout.Space();

            base.OnInspectorGUI();
        }
        private void DrawTopButtons()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("重新绑定组件", GUILayout.Height(25)))
                {
                    UIGenerateQuick.UIGenerateBind();
                }

                if (GUILayout.Button("复制绑定属性", GUILayout.Height(25)))
                {
                    UIGenerateQuick.UICopyBindVariableContent();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}