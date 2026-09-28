using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Editor
{
    public class ProjectFindByGuid
    {
        private static System.Type tProjectBrowser = System.Type.GetType("UnityEditor.ProjectBrowser,UnityEditor.dll");

        private static FieldInfo m_SearchFieldText =
            tProjectBrowser.GetField("m_SearchFieldText", BindingFlags.Instance | BindingFlags.NonPublic);

        private static IMGUIContainer _container = new IMGUIContainer(OnGUI);
        private static EditorWindow projectWindow;

        [InitializeOnLoadMethod]
        public static void OnLoad()
        {
            AssemblyReloadEvents.afterAssemblyReload += Reload;
            Reload();
        }

        private static void Reload()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling)
            {
                EditorApplication.update -= Update;
                return;
            }


            if (EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType() == tProjectBrowser)
            {
                projectWindow = EditorWindow.focusedWindow;
                if (!projectWindow.rootVisualElement.parent.hierarchy.Children().Contains(_container))
                {
                    projectWindow.rootVisualElement.parent.hierarchy.Insert(1, _container);
                    _container.StretchToParentWidth();
                }
            }

            if (projectWindow != null & !string.IsNullOrEmpty(errorMsg))
            {
                projectWindow.Repaint();
            }
        }

        private static GUIContent btnTitle;
        private static GUIStyle btnStyle;
        private static GUIStyle errorStyle;
        private static string errorMsg;
        private static double errorMsgStartTime;

        static void OnGUI()
        {
            if (projectWindow == null)
            {
                return;
            }

            btnTitle = btnTitle ?? new GUIContent("搜索GUID",
                EditorGUIUtility.IconContent("d_search_icon").image);
            // AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.FindAssets("ty_icon_fangdajing_1 t:Texture2D").Select(AssetDatabase.GUIDToAssetPath).First()));

            btnStyle = btnStyle ?? new GUIStyle(GUI.skin.button);
            var calcSize = GUI.skin.button.CalcSize(btnTitle);

            Rect rect = new Rect(new Vector2(0, 22 - calcSize.y), calcSize);
            float xMin = 45;
            float xRight = 389;
            if ((int)tProjectBrowser.GetField("m_ViewMode", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(projectWindow) == 1)
            {
                xRight = 415;
            }

            if (Screen.width < xMin + xRight)
            {
                rect.x = xMin;
            }
            else
            {
                rect.x = Screen.width - xRight;
            }

            _container.style.height = rect.y + rect.height;
            var value = (string)m_SearchFieldText.GetValue(projectWindow);
            if (!string.IsNullOrEmpty(value))
            {
                if (GUI.Button(rect, btnTitle))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(value);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        GUI.FocusControl(null);
                        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                    else
                    {
                        errorMsg = $"未找到GUID=[{value}] 的资源，请在搜索框输入正确的GUID";
                        errorMsgStartTime = EditorApplication.timeSinceStartup;
                        Debug.LogError(errorMsg);
                    }
                }
            }


            if (!string.IsNullOrEmpty(errorMsg))
            {
                var lerp = Mathf.Clamp01(1 - (float)(EditorApplication.timeSinceStartup - errorMsgStartTime - 1));

                if (lerp <= 0)
                {
                    errorMsg = null;
                    return;
                }

                errorStyle = errorStyle ?? "OL Ping";
                Color color = Color.white;
                color.a = lerp;
                GUI.color = color;
                GUI.backgroundColor = color;
                rect.size = errorStyle.CalcSize(new GUIContent(errorMsg));
                rect.x += calcSize.x + 5;
                GUI.Label(rect, errorMsg, errorStyle);
            }

            GUI.backgroundColor = Color.white;
            GUI.color = Color.white;
        }
    }
}