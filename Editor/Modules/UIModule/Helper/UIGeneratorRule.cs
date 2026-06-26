using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    public interface IUIIdentifierFormatter
    {
        string GetPrivateComponentName(string regexName, string componentName, EBindType bindType);

        string GetPublicComponentName(string variableName);

        string GetClassName(GameObject targetObject);
    }

    public interface IUIResourcePathResolver
    {
        string GetResourcePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData);

        bool CanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData);
    }

    public interface IUIScriptCodeEmitter
    {
        string GetReferenceNamespaces(List<UIBindData> uiBindData);

        string GetVariableContent(List<UIBindData> uiBindData, Func<string, string> publicNameFactory);
    }

    public interface IUIScriptFileWriter
    {
        void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData);
    }

    public sealed class DefaultUIIdentifierFormatter : IUIIdentifierFormatter
    {
        private static readonly HashSet<string> s_CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        public string GetPrivateComponentName(string regexName, string componentName, EBindType bindType)
        {
            var endPrefix = bindType == EBindType.ListCom ? "List" : string.Empty;
            var endNameIndex = componentName.IndexOf(UIGeneratorSettings.ComCheckEndName, StringComparison.Ordinal);
            var componentSuffix = endNameIndex >= 0 ? componentName.Substring(endNameIndex + 1) : componentName;
            var fieldName = $"m_{NormalizeIdentifier(regexName)}{NormalizeIdentifier(componentSuffix)}{endPrefix}";
            return MakeSafeIdentifier(string.IsNullOrWhiteSpace(fieldName) ? "m_Component" : fieldName);
        }

        public string GetPublicComponentName(string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                return variableName;
            }

            var publicName = variableName.StartsWith("m_", StringComparison.Ordinal) && variableName.Length > 2
                ? variableName.Substring(2)
                : variableName;

            publicName = NormalizeIdentifier(publicName);
            return MakeSafeIdentifier(string.IsNullOrEmpty(publicName) ? "Component" : publicName);
        }

        public string GetClassName(GameObject targetObject)
        {
            var objectName = NormalizeIdentifier(targetObject.name);
            var className = $"{objectName}";
            return MakeSafeIdentifier(string.IsNullOrEmpty(className) ? "View" : className);
        }

        private static string NormalizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var parts = Regex.Split(value, "[^A-Za-z0-9_]+")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (parts.Length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var part in parts)
            {
                builder.Append(part[0]);
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }

            return builder.ToString();
        }

        private static string MakeSafeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "_";
            }

            var safeIdentifier = identifier;
            if (char.IsDigit(safeIdentifier[0]))
            {
                safeIdentifier = "_" + safeIdentifier;
            }

            if (s_CSharpKeywords.Contains(safeIdentifier))
            {
                safeIdentifier += "_";
            }

            return safeIdentifier;
        }
    }

    public sealed class DefaultUIResourcePathResolver : IUIResourcePathResolver
    {
        public string GetResourcePath(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null)
            {
                return $"\"{nameof(targetObject)}\"";
            }

            var defaultPath = targetObject.name;
            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return defaultPath;
            }

            assetPath = assetPath.Replace('\\', '/');

            return scriptGenerateData.FromResources ?
                GetResourcesPath(assetPath, scriptGenerateData, defaultPath) :
                GetAssetBundlePath(assetPath, scriptGenerateData, defaultPath);
        }

        public bool CanGenerate(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null || scriptGenerateData == null)
            {
                return false;
            }

            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return false;
            }

            assetPath = assetPath.Replace('\\', '/');
            var isValidPath = assetPath.StartsWith(scriptGenerateData.UIPrefabRootPath, StringComparison.OrdinalIgnoreCase);
            if (!isValidPath)
            {
                Debug.LogWarning(
                    $"UI asset path does not match UIGenerateConfiguration.UIPrefabRootPath.\n[AssetPath]{assetPath}\n[ConfigPath]{scriptGenerateData.UIPrefabRootPath}");
            }

            return isValidPath;
        }

        private static string GetResourcesPath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            var resourcesRoot = scriptGenerateData.UIPrefabRootPath;
            var relPath = GetResourcesRelativePath(assetPath, resourcesRoot);
            if (relPath == null)
            {
                Debug.LogWarning($"[UI Generate] Resource {assetPath} is not under configured Resources root: {resourcesRoot}");
                return defaultPath;
            }

            return relPath;
        }

        private static string GetAssetBundlePath(string assetPath, UIScriptGenerateData scriptGenerateData, string defaultPath)
        {
            try
            {
                var defaultPackage = YooAsset.Editor.AssetBundleCollectorSettingData.Setting.GetPackage("DefaultPackage");
                if (defaultPackage?.EnableAddressable == true)
                {
                    return defaultPath;
                }
            }
            catch
            {
                // ignored
            }

            var bundleRoot = scriptGenerateData.UIPrefabRootPath;
            if (!assetPath.StartsWith(bundleRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[UI Generate] Resource {assetPath} is not under configured AssetBundle root: {bundleRoot}");
                return defaultPath;
            }

            return Path.ChangeExtension(assetPath, null);
        }

        private static string GetResourcesRelativePath(string assetPath, string resourcesRoot)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourcesRoot))
            {
                return null;
            }

            assetPath = assetPath.Replace('\\', '/');
            resourcesRoot = resourcesRoot.Replace('\\', '/');

            if (!assetPath.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relPath = assetPath.Substring(resourcesRoot.Length).TrimStart('/');
            return Path.ChangeExtension(relPath, null);
        }
    }

    public sealed class DefaultUIScriptCodeEmitter : IUIScriptCodeEmitter
    {
        public string GetReferenceNamespaces(List<UIBindData> uiBindData)
        {
            var namespaceSet = new HashSet<string>(StringComparer.Ordinal) { "UnityEngine" };

            uiBindData?
                .Where(bindData => bindData?.GetFirstOrDefaultType() != null)
                .Select(bindData => bindData.GetFirstOrDefaultType()?.Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns))
                .ToList()
                .ForEach(ns => namespaceSet.Add(ns));

            return string.Join(Environment.NewLine, namespaceSet.OrderBy(ns => ns, StringComparer.Ordinal).Select(ns => $"using {ns};"));
        }

        public string GetVariableContent(List<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            if (uiBindData == null || uiBindData.Count == 0)
            {
                return string.Empty;
            }

            var declarations = uiBindData
                .Where(bindData => bindData != null && !string.IsNullOrEmpty(bindData.Name))
                .OrderBy(bindData => bindData.Name, StringComparer.Ordinal)
                .Select(bindData => GenerateVariableDeclaration(bindData, publicNameFactory))
                .Where(content => !string.IsNullOrEmpty(content));

            return string.Join(Environment.NewLine + Environment.NewLine, declarations);
        }

        private static string GenerateVariableDeclaration(UIBindData bindData, Func<string, string> publicNameFactory)
        {
            var variableName = bindData.Name;
            var publicName = publicNameFactory(variableName);
            var firstType = bindData.GetFirstOrDefaultType();
            var typeName = firstType?.Name ?? "Component";

            var declaration = new StringBuilder();
            declaration.Append("\t\t[SerializeField]");

            switch (bindData.BindType)
            {
                case EBindType.None:
                case EBindType.Widget:
                    declaration.AppendLine($" private {typeName} {variableName};");
                    declaration.Append($"\t\tpublic {typeName} {publicName} => {variableName};");
                    break;

                case EBindType.ListCom:
                    var count = Math.Max(0, bindData.Objs?.Count ?? 0);
                    declaration.AppendLine($" private {typeName}[] {variableName} = new {typeName}[{count}];");
                    declaration.Append($"\t\tpublic {typeName}[] {publicName} => {variableName};");
                    break;
            }

            return declaration.ToString();
        }
    }

    public sealed class DefaultUIScriptFileWriter : IUIScriptFileWriter
    {
        public void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (scriptContent == null) throw new ArgumentNullException(nameof(scriptContent));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            var scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            var scriptFilePath = Path.Combine(scriptFolderPath, $"{className}.g.cs");

            Directory.CreateDirectory(scriptFolderPath);

            #region 自动生成脚本

            // TODO 自动订阅必要组件事件？
            scriptContent = scriptContent.Replace("#Controller#", @"
    public partial class #ClassName#
    {
        private #ClassName#Binder _bindComponent;

        protected override void ScriptGenerator()
		{
			_bindComponent = gameObject.GetComponent<#ClassName#Binder>();
			if(_bindComponent == null)
			{
				Log.Error($""根物体: {gameObject.name} 缺少组件 #ClassName#Binder, 请检查！！！"");
				return;
            }
		}
    }".Replace("#ClassName#", className));

            if (File.Exists(scriptFilePath) && IsContentUnchanged(scriptFilePath, scriptContent))
            {
                UIScriptGeneratorHelper.BindUIScript();
                return;
            }

            File.WriteAllText(scriptFilePath, scriptContent, Encoding.UTF8);

            #endregion

            #region 窗口实现类模板

            var windowFilePath = Path.Combine(scriptFolderPath, $"{className}.cs");
            if (!File.Exists(windowFilePath))
            {
                string windowScript = @"using Moirai.Atropos.UI;

namespace GameLogic.UI
{
    [Window(UILayer.UI)]
    public partial class #ClassName# : UIWindow
    {
    }
}".Replace("#ClassName#", className);

                File.WriteAllText(windowFilePath, windowScript, Encoding.UTF8);
            }

            #endregion

            AssetDatabase.Refresh();
        }

        private static bool IsContentUnchanged(string filePath, string newContent)
        {
            var oldText = File.ReadAllText(filePath, Encoding.UTF8);
            return oldText.Equals(newContent, StringComparison.Ordinal);
        }
    }
}