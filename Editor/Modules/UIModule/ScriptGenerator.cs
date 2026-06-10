using System;
using System.Linq;
using System.Text;
using Moirai.Atropos.UI;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.ModulesUI
{
    public static partial class ScriptGenerator
    {
        private static string[] s_VariableNameRegex;
        private static void CheckVariableNames()
        {
            var cnt = Enum.GetValues(typeof(UIFieldCodeStyle)).Length;
            s_VariableNameRegex = new string[cnt];

            for (int i = 0; i < cnt; i++)
            {
                s_VariableNameRegex[i] = ScriptGeneratorSettings.GetPrefixNameByCodeStyle((UIFieldCodeStyle)i);
            }
        }

        private const string GAP = "/";

        [MenuItem("GameObject/ScriptGenerator/UIProperty", priority = 41)]
        public static void MemberProperty()
        {
            GenerateAndCopy(false);
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty", true)]
        public static bool ValidateMemberProperty()
        {
            return !ScriptGeneratorSettings.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty - UniTask", priority = 43)]
        public static void MemberPropertyUniTask()
        {
            GenerateAndCopy(false, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIProperty - UniTask", true)]
        public static bool ValidateMemberPropertyUniTask()
        {
            return !ScriptGeneratorSettings.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener", priority = 42)]
        public static void MemberPropertyAndListener()
        {
            GenerateAndCopy(true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener", true)]
        public static bool ValidateMemberPropertyAndListener()
        {
            return !ScriptGeneratorSettings.UseBindComponent;
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener - UniTask", priority = 44)]
        public static void MemberPropertyAndListenerUniTask()
        {
            GenerateAndCopy(true, true);
        }

        [MenuItem("GameObject/ScriptGenerator/UIPropertyAndListener - UniTask", true)]
        public static bool ValidateMemberPropertyAndListenerUniTask()
        {
            return !ScriptGeneratorSettings.UseBindComponent;
        }

        private static void GenerateAndCopy(bool includeListener, bool isUniTask = false)
        {
            string str = Generate(includeListener, isUniTask, Selection.activeTransform);
            if (string.IsNullOrEmpty(str))
            {
                Debug.LogError("出错啦，请检查是否选中root为空");
            }
            else
            {
                TextEditor te = new TextEditor();
                te.text = str;
                te.SelectAll();
                te.Copy();

                Debug.Log($"脚本已生成到剪贴板，请自行Ctl+V粘贴");
            }
        }

        internal static string Generate(bool includeListener, bool isUniTask = false, Transform root = null, string className = "")
        {
            if (root == null) return string.Empty;

            CheckVariableNames();

            StringBuilder strVar = new StringBuilder();
            StringBuilder strBind = new StringBuilder();
            StringBuilder strOnCreate = new StringBuilder();
            StringBuilder strCallback = new StringBuilder();
            Ergodic(root, root, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
            StringBuilder strFile = new StringBuilder();

            #region 生成头文件与类名
            strFile.Append($"using {typeof(UIWindow).Namespace};\n");
            strFile.Append("using UnityEngine;\n");
            strFile.Append("using UnityEngine.UI;\n");
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
            strFile.Append("using TMPro;\n");
#endif
            if (isUniTask)
            {
                strFile.Append("using Cysharp.Threading.Tasks;\n");
            }

            strFile.Append("\n");
            strFile.Append($"namespace {ScriptGeneratorSettings.Namespace}\n");
            strFile.Append("{\n");

            var widgetPrefix = $"{(ScriptGeneratorSettings.CodeStyle == UIFieldCodeStyle.MPrefix ? "m_" : "_")}{ScriptGeneratorSettings.WidgetName}";
            if (root.name.StartsWith(widgetPrefix))
            {
                strFile.Append("\tclass " + (string.IsNullOrEmpty(className) ? root.name.Replace(widgetPrefix, "") : className) + " : UIWidget\n");
            }
            else
            {
                strFile.Append($"\t[Window(UILayer.UI, location:\"{root.name}\")]\n");
                strFile.Append("\tclass " + (string.IsNullOrEmpty(className) ? root.name : className) + " : UIWindow\n");
            }

            strFile.Append("\t{\n");
            #endregion

            strFile.Append("\n");
            // 脚本工具生成的代码
            strFile.Append("\t\t#region 脚本工具生成的代码\n");
            strFile.Append("\n");
            strFile.Append(strVar);
            strFile.Append("\n");
            strFile.Append("\t\tprotected override void ScriptGenerator()\n");
            strFile.Append("\t\t{\n");
            strFile.Append(strBind);
            strFile.Append(strOnCreate);
            strFile.Append("\t\t}\n");
            strFile.Append("\n");
            strFile.Append("\t\t#endregion");

            if (includeListener)
            {
                strFile.Append("\n\n");
                // #region 事件
                if (strCallback.Length > 0)
                {
                    strFile.Append("\t\t#region 事件\n");
                    strFile.Append(strCallback);
                    strFile.Append("\t\t#endregion\n\n");
                }
            }

            strFile.Append("\t}\n");
            strFile.Append("}");
            return strFile.ToString();
        }

        private static void Ergodic(Transform root, Transform transform, ref StringBuilder strVar, ref StringBuilder strBind, ref StringBuilder strOnCreate,
            ref StringBuilder strCallback, bool isUniTask)
        {
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                WriteScript(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
                if (child.name.StartsWith(ScriptGeneratorSettings.WidgetName))
                {
                    // 子 Item 不再往下遍历
                    continue;
                }

                Ergodic(root, child, ref strVar, ref strBind, ref strOnCreate, ref strCallback, isUniTask);
            }
        }

        private static string GetRelativePath(Transform child, Transform root)
        {
            StringBuilder path = new StringBuilder();
            path.Append(child.name);
            while (child.parent != null && child.parent != root)
            {
                child = child.parent;
                path.Insert(0, GAP);
                path.Insert(0, child.name);
            }

            return path.ToString();
        }

        /// <summary>
        /// 获取事件函数名
        /// </summary>
        /// <param name="varName">组件的变量名</param>
        /// <param name="triggerName">组件触发动作，如 Click、Change</param>
        /// <param name="componentName">组件类型名称</param>
        /// <param name="componentAbbr">组件缩写。若为空，则为 <see cref="componentName"/></param>
        /// <returns></returns>
        public static string GetEventFuncName(string varName, string triggerName, string componentName, string componentAbbr = "")
        {
            if (string.IsNullOrEmpty(varName)) return varName;

            for (int i = 0; i < s_VariableNameRegex.Length; i++)
            {
                var prefix = s_VariableNameRegex[i];
                if (varName.StartsWith(prefix))
                {
                    return $"On{triggerName}{varName.Replace(prefix + ScriptGeneratorSettings.GetUIComponentWithoutPrefixName(componentName), string.Empty)}{(string.IsNullOrEmpty(componentAbbr) ? componentName : componentAbbr)}";
                }
            }

            return varName;
        }

        private static void WriteScript(Transform root, Transform child, ref StringBuilder strVar, ref StringBuilder strBind, ref StringBuilder strOnCreate,
            ref StringBuilder strCallback, bool isUniTask)
        {
            string varName = child.name;

            var rule = ScriptGeneratorSettings.ScriptGenerateRules
                .Find(r => varName.StartsWith(r.UIElementRegex));
            if (rule == null) return;

            var componentName = rule.ComponentName;
            if (string.IsNullOrEmpty(componentName)) return;

            varName = GetVariableName(varName);
            if (string.IsNullOrEmpty(varName)) return;

            strVar.AppendLine($"\t\tprivate {componentName} {varName}{(ScriptGeneratorSettings.NullableEnable ? " = null!;" : ";")}");

            string varPath = GetRelativePath(child, root);
            switch (componentName)
            {
                case "Transform":
                    strBind.AppendLine($"\t\t\t{varName} = FindChild(\"{varPath}\");");
                    break;
                case "GameObject":
                    strBind.AppendLine($"\t\t\t{varName} = FindChild(\"{varPath}\").gameObject;");
                    break;
                default:
                    if (rule.IsUIWidget)
                    {
                        strBind.AppendLine($"\t\t\t{varName} = CreateWidget<{componentName}>(\"{varPath}\");");
                    }
                    else
                    {
                        strBind.AppendLine($"\t\t\t{varName} = FindChildComponent<{componentName}>(\"{varPath}\");");
                    }
                    break;
            }

            switch (componentName)
            {
                case "Button":
                {
                    string varFuncName = GetEventFuncName(varName, "Click", componentName, "Btn");
                    if (isUniTask)
                    {
                        strOnCreate.AppendLine($"\t\t\t{varName}.onClick.AddListener(UniTask.UnityAction({varFuncName}));");
                        strCallback.AppendLine($"\t\tprivate async UniTaskVoid {varFuncName}()");
                        strCallback.AppendLine("\t\t{\n await UniTask.Yield();\n\t\t}");
                    }
                    else
                    {
                        strOnCreate.AppendLine($"\t\t\t{varName}.onClick.AddListener({varFuncName});");
                        strCallback.AppendLine($"\t\tprivate void {varFuncName}()");
                        strCallback.AppendLine("\t\t{\n\t\t}");
                    }

                    break;
                }

                case "Toggle":
                {
                    string varFuncName = GetEventFuncName(varName, "Change", componentName);
                    strOnCreate.AppendLine($"\t\t\t{varName}.onValueChanged.AddListener({varFuncName});");
                    strCallback.AppendLine($"\t\tprivate void {varFuncName}(bool isOn)");
                    strCallback.AppendLine("\t\t{\n\t\t}");
                    break;
                }

                case "Slider":
                {
                    string varFuncName = GetEventFuncName(varName, "Change", componentName);
                    strOnCreate.AppendLine($"\t\t\t{varName}.onValueChanged.AddListener({varFuncName});");
                    strCallback.AppendLine($"\t\tprivate void {varFuncName}(float value)");
                    strCallback.AppendLine("\t\t{\n\t\t}");
                    break;
                }

                case "TMP_Dropdown":
                {
                    var tmpDropdownFuncName = GetEventFuncName(varName, "Change", componentName, "Dropdown");
                    strOnCreate.AppendLine($"\t\t\t{varName}.onValueChanged.AddListener({tmpDropdownFuncName});");
                    strCallback.AppendLine($"\t\tprivate partial void {tmpDropdownFuncName}(int selectedIndex);");
                    strCallback.AppendLine();
                    break;
                }

                // 框架组件

                case "ButtonSuper":
                {
                    string varFuncName = GetEventFuncName(varName, "Click", componentName, "Btn");
                    if (isUniTask)
                    {
                        strOnCreate.AppendLine($"\t\t\t{varName}.onClick.AddListener(UniTask.UnityAction({varFuncName}));");
                        strCallback.AppendLine($"\t\tprivate async UniTaskVoid {varFuncName}()");
                        strCallback.AppendLine("\t\t{\n await UniTask.Yield();\n\t\t}");
                    }
                    else
                    {
                        strOnCreate.AppendLine($"\t\t\t{varName}.onClick.AddListener({varFuncName});");
                        strCallback.AppendLine($"\t\tprivate void {varFuncName}()");
                        strCallback.AppendLine("\t\t{\n\t\t}");
                    }

                    break;
                }

                case "UIMenuItem":
                {
                    string varFuncName = GetEventFuncName(varName, "Click", componentName, "Btn");
                    strOnCreate.AppendLine($"\t\t\t{varName}.onSubmit.AddListener({varFuncName});");
                    strCallback.AppendLine($"\t\tprivate void {varFuncName}(UIMenuItem item)");
                    strCallback.AppendLine("\t\t{\n\t\t}");
                    break;
                }

                case "SlideToggle":
                {
                    string varFuncName = GetEventFuncName(varName, "Change", componentName);
                    strOnCreate.AppendLine($"\t\t\t{varName}.OnValueChanged.AddListener({varFuncName});");
                    strCallback.AppendLine($"\t\tprivate void {varFuncName}(bool isOn)");
                    strCallback.AppendLine("\t\t{\n\t\t}");
                    break;
                }
            }
        }

        private static string GetUIWidgetGameObjectName()
        {
            foreach (var rule in ScriptGeneratorSettings.ScriptGenerateRules.Where(rule => rule.IsUIWidget))
            {
                return rule.UIElementRegex;
            }
            // 生成规则里没有有勾选是否Widget时，保底
            return GetUIWidgetName();
        }

        /// <summary>
        /// 获取符合命名规则的UIWidget组件变量名
        /// </summary>
        /// <returns></returns>
        internal static string GetUIWidgetName()
        {
            return GetComponentName(ScriptGeneratorSettings.WidgetName);
        }

        /// <summary>
        /// 根据组件名称获取符合命名规则的变量名
        /// </summary>
        /// <param name="componentName"></param>
        /// <returns></returns>
        private static string GetComponentName(string componentName)
        {
            return ScriptGeneratorSettings.GetPrefixNameByCodeStyle(ScriptGeneratorSettings.CodeStyle) + componentName;
        }

        /// <summary>
        /// 删除变量名中的前缀
        /// </summary>
        /// <param name="varName"></param>
        /// <returns></returns>
        private static string GetVariableName(string varName)
        {
            if (string.IsNullOrEmpty(varName))
            {
                return varName;
            }

            foreach (var prefix in s_VariableNameRegex)
            {
                if (varName.StartsWith(prefix))
                {
                    varName = varName[prefix.Length..];
                    varName = GetComponentName(varName);
                    break;
                }
            }
            return varName;
        }
    }
    
    public class GeneratorHelper : EditorWindow
    {
        [MenuItem("GameObject/ScriptGenerator/About", priority = 49)]
        public static void About()
        {
            var window = GetWindow<GeneratorHelper>();
            window.titleContent = new GUIContent("About", EditorGUIUtility.IconContent("_Help").image);
            window.minSize = new Vector2(400, 400);
        }

        public void Awake()
        {
            minSize = new Vector2(400, 600);
        }

        protected void OnGUI()
        {
            GUILayout.BeginVertical();
            foreach (var rule in ScriptGeneratorSettings.ScriptGenerateRules)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(rule.UIElementRegex, GUILayout.Width(150));
                GUILayout.Label("<=>", GUILayout.Width(50));
                GUILayout.Label(rule.ComponentName.ToString(), GUILayout.Width(150));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }
    }
}