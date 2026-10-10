using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Moirai.Atropos.Input;
using Sirenix.OdinInspector.Editor;

namespace Moirai.Atropos.Editor.Input
{
    [CustomEditor(typeof(InputActionsConfiguration))]
    public class InputActionsConfigurationEditor : OdinEditor
    {
        private const string TEMPLATE_FILE_NAME = "template-input-actions";
        private InputActionsConfiguration _target;

        protected override void OnEnable()
        {
            base.OnEnable();

            _target = (InputActionsConfiguration)target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.HelpBox(
                $"单击该按钮替换原始的 “{_target.m_ClassName}.cs” 文件。用于需要在不修改代码的情况下创建自定义动作。",
                MessageType.None
            );

            if (GUILayout.Button("Generate C# Class"))
            {
                bool result = EditorUtility.DisplayDialog(
                    "Create Actions",
                    "警告： 生成并覆盖 C# 脚本。\n确定要继续吗？", "Yes", "No");

                if (!result) return;

                string cSharpScriptPath = PathUtility.TruncatePath(AssetDatabase.GetAssetPath(target), 1) + "/" + _target.m_ClassName;
                cSharpScriptPath += ".cs";

                string templatePath = AssetDatabase.FindAssets(TEMPLATE_FILE_NAME)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(path => path.Contains($"{TEMPLATE_FILE_NAME}.txt"));

                Debug.Log($"cSharpScriptPath: {cSharpScriptPath}\ntemplatePath: {templatePath}");
                CreateCSharpClass(cSharpScriptPath, templatePath);
            }
        }

        private void CreateCSharpClass(string cSharpScriptPath, string templatePath)
        {
            if (cSharpScriptPath == null || templatePath == null) return;

            string output = GenerateOutput(templatePath);

            FileStream fileStream = File.Exists(cSharpScriptPath) ?
                File.Open(cSharpScriptPath, FileMode.Truncate, FileAccess.ReadWrite) :
                File.Create(cSharpScriptPath);

            StreamWriter file = new StreamWriter(fileStream);

            file.Write(output);
            file.Close();

            AssetDatabase.Refresh();
        }


        private string GenerateOutput(string templatePath)
        {            
            StreamReader reader = new StreamReader(templatePath);

            string output = reader.ReadToEnd();
            reader.Close();

            output = Regex.Replace(output, @"@\s*namespace\s*@", _target.m_Namespace);
            output = Regex.Replace(output, @"@\s*struct-name\s*@", _target.m_ClassName);
            output = Regex.Replace(output, @"@\s*actions-group\s*@", _target.m_ActionsGroup);
            
            // -----------------------------------------------------------------------------------------------------------------------------------
            // Bool Actions ----------------------------------------------------------------------------------------------------------------------
            // -----------------------------------------------------------------------------------------------------------------------------------

            string definitionsString = "";
            string resetString = "";
            string newString = "";
            string setValueString = "";
            string copyValueString = "";
            string updateString = "";

            for (int i = 0; i < _target.m_BoolActions.Length; i++)
            {
                string actionName = _target.m_BoolActions[i];
                if (string.IsNullOrEmpty(actionName)) continue;

                string variableName = "@";
                variableName += actionName.Replace(" ", "");
                
                definitionsString += "\t\tpublic BoolAction " + variableName + ";\n";
                resetString += "\t\t\t" + variableName + ".Reset();\n";
                newString += "\t\t\t" + variableName + " = new BoolAction();\n" +
                    "\t\t\t" + variableName + ".Initialize();\n\n";
                setValueString += "\t\t\t" + variableName + $".Value = inputHandler.GetBool(\"{actionName}\", ACTIONS_GROUP);\n";
                copyValueString += "\t\t\t" + variableName + ".Value = characterActions." + variableName.Substring(1) + ".Value;\n";
                updateString += "\t\t\t" + variableName + ".Update(dt);\n";
            }

            // Write bool actions
            output = Regex.Replace(output, @"@\s*bool-actions-definitions\s*@", definitionsString);
            output = Regex.Replace(output, @"@\s*bool-actions-reset\s*@", resetString);
            output = Regex.Replace(output, @"@\s*bool-actions-new\s*@", newString);
            output = Regex.Replace(output, @"@\s*bool-actions-setValue\s*@", setValueString);
            output = Regex.Replace(output, @"@\s*bool-actions-copyValue\s*@", copyValueString);
            output = Regex.Replace(output, @"@\s*bool-actions-update\s*@", updateString);

            // -----------------------------------------------------------------------------------------------------------------------------------
            // Float Actions ---------------------------------------------------------------------------------------------------------------------
            // -----------------------------------------------------------------------------------------------------------------------------------

            definitionsString = "";
            resetString = "";
            newString = "";
            setValueString = "";
            copyValueString = "";
            updateString = "";
                        
            for (int i = 0; i < _target.m_FloatActions.Length; i++)
            {
                string actionName = _target.m_FloatActions[i];
                if (string.IsNullOrEmpty(actionName)) continue;

                string variableName = "@";
                variableName += actionName.Replace(" ", "");
                
                definitionsString += "\t\tpublic FloatAction " + variableName + ";\n";
                resetString += "\t\t\t" + variableName + ".Reset();\n";
                newString += "\t\t\t" + variableName + " = new FloatAction();\n";
                setValueString += "\t\t\t" + variableName + $".Value = inputHandler.GetFloat(\"{actionName}\", ACTIONS_GROUP);\n";
                copyValueString += "\t\t\t" + variableName + ".Value = characterActions." + variableName.Substring(1) + ".Value;\n";
            }

            // Write bool actions
            output = Regex.Replace(output, @"@\s*float-actions-definitions\s*@", definitionsString);
            output = Regex.Replace(output, @"@\s*float-actions-reset\s*@", resetString);
            output = Regex.Replace(output, @"@\s*float-actions-new\s*@", newString);
            output = Regex.Replace(output, @"@\s*float-actions-setValue\s*@", setValueString);
            output = Regex.Replace(output, @"@\s*float-actions-copyValue\s*@", copyValueString);

            // -----------------------------------------------------------------------------------------------------------------------------------
            // Vector2 Actions -------------------------------------------------------------------------------------------------------------------
            // -----------------------------------------------------------------------------------------------------------------------------------

            definitionsString = "";
            resetString = "";
            newString = "";
            setValueString = "";
            copyValueString = "";
            updateString = "";

            for (int i = 0; i < _target.m_Vector2Actions.Length; i++)
            {
                string actionName = _target.m_Vector2Actions[i];
                if (string.IsNullOrEmpty(actionName)) continue;

                string variableName = "@";
                variableName += actionName.Replace(" ", "");
                
                definitionsString += "\t\tpublic Vector2Action " + variableName + ";\n";
                resetString += "\t\t\t" + variableName + ".Reset();\n";
                newString += "\t\t\t" + variableName + " = new Vector2Action();\n";
                setValueString += "\t\t\t" + variableName + $".Value = inputHandler.GetVector2(\"{actionName}\", ACTIONS_GROUP);\n";
                copyValueString += "\t\t\t" + variableName + ".Value = characterActions." + variableName.Substring(1) + ".Value;\n";

            }

            // Write bool actions
            output = Regex.Replace(output, @"@\s*vector2-actions-definitions\s*@", definitionsString);
            output = Regex.Replace(output, @"@\s*vector2-actions-reset\s*@", resetString);
            output = Regex.Replace(output, @"@\s*vector2-actions-new\s*@", newString);
            output = Regex.Replace(output, @"@\s*vector2-actions-setValue\s*@", setValueString);
            output = Regex.Replace(output, @"@\s*vector2-actions-copyValue\s*@", copyValueString);

            return output;
        }
    }
}