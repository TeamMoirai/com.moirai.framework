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

        string GetControllerContent(string className, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory);

        string GetWindowContent(string className, string nameSpace, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory);
    }

    public interface IUIScriptFileWriter
    {
        void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData, string windowScriptContent = null);
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
        private const string BINDER_VARIABLE_NAME = "_bindComponent";

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

        public string GetControllerContent(string className, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            var propertyAccessors = GeneratePropertyAccessors(uiBindData, publicNameFactory);
            var eventSubscriptions = GenerateEventSubscriptions(uiBindData, publicNameFactory);
            var eventHandlers = GenerateEventHandlers(uiBindData, publicNameFactory);

            var controllerContent = new StringBuilder();
            controllerContent.AppendLine();
            controllerContent.AppendLine($"    public partial class {className}");
            controllerContent.AppendLine("    {");
            controllerContent.AppendLine($"        private {className}Binder {BINDER_VARIABLE_NAME};");

            if (!string.IsNullOrEmpty(propertyAccessors))
            {
                controllerContent.AppendLine();
                controllerContent.Append(propertyAccessors);
            }

            controllerContent.AppendLine();
            controllerContent.AppendLine("        protected override void ScriptGenerator()");
            controllerContent.AppendLine("\t\t{");
            controllerContent.AppendLine($"\t\t\t{BINDER_VARIABLE_NAME} = gameObject.GetComponent<{className}Binder>();");
            controllerContent.AppendLine($"\t\t\tif({BINDER_VARIABLE_NAME} == null)");
            controllerContent.AppendLine("\t\t\t{");
            controllerContent.AppendLine($"\t\t\t\tLog.Error($\"根物体: {{gameObject.name}} 缺少组件 {className}Binder, 请检查！！！\");");
            controllerContent.AppendLine("\t\t\t\treturn;");
            controllerContent.AppendLine("            }");

            if (!string.IsNullOrEmpty(eventSubscriptions))
            {
                controllerContent.AppendLine();
                controllerContent.Append(eventSubscriptions);
            }

            controllerContent.AppendLine("\t\t}");

            if (!string.IsNullOrEmpty(eventHandlers))
            {
                controllerContent.AppendLine();
                controllerContent.AppendLine("\t\t#region 事件 [EVENTS]");
                controllerContent.AppendLine(eventHandlers);
                controllerContent.AppendLine("\t\t#endregion");
            }

            controllerContent.Append("    }");

            return controllerContent.ToString();
        }

        private static string GeneratePropertyAccessors(IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            if (uiBindData == null || uiBindData.Count == 0)
            {
                return string.Empty;
            }

            var accessors = new StringBuilder();
            foreach (var bindData in uiBindData.Where(d => d != null && !string.IsNullOrEmpty(d.Name)))
            {
                var publicName = publicNameFactory(bindData.Name);
                var firstType = bindData.GetFirstOrDefaultType();
                var typeName = firstType?.Name ?? "Component";
                var privateName = "_" + char.ToLowerInvariant(publicName[0]) + publicName.Substring(1);

                if (bindData.BindType == EBindType.ListCom)
                {
                    accessors.AppendLine($"\t\tprivate {typeName}[] {privateName} => {BINDER_VARIABLE_NAME}.{publicName};");
                }
                else
                {
                    accessors.AppendLine($"\t\tprivate {typeName} {privateName} => {BINDER_VARIABLE_NAME}.{publicName};");
                }
            }

            return accessors.ToString();
        }

        private static string GenerateEventSubscriptions(IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            if (uiBindData == null || uiBindData.Count == 0)
            {
                return string.Empty;
            }

            var eventConfigs = UIGeneratorSettings.UIEventBindingConfigs;
            if (eventConfigs == null || eventConfigs.Count == 0)
            {
                return string.Empty;
            }

            var subscriptions = new StringBuilder();
            foreach (var bindData in uiBindData.Where(d => d != null && !string.IsNullOrEmpty(d.Name)))
            {
                if (bindData.BindType == EBindType.ListCom)
                {
                    continue;
                }

                var firstType = bindData.GetFirstOrDefaultType();
                if (firstType == null)
                {
                    continue;
                }

                var config = FindEventConfig(firstType.FullName, eventConfigs);
                if (config == null)
                {
                    continue;
                }

                var publicName = publicNameFactory(bindData.Name);
                var privateName = "_" + char.ToLowerInvariant(publicName[0]) + publicName.Substring(1);
                var eventName = GetEventFuncName(publicName, config);

                subscriptions.AppendLine($"\t\t\t{privateName}.{config.EventMember}.AddListener({eventName});");
            }

            return subscriptions.ToString();
        }

        private static string GenerateEventHandlers(IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            if (uiBindData == null || uiBindData.Count == 0)
            {
                return string.Empty;
            }

            var eventConfigs = UIGeneratorSettings.UIEventBindingConfigs;
            if (eventConfigs == null || eventConfigs.Count == 0)
            {
                return string.Empty;
            }

            var handlers = new StringBuilder();
            foreach (var bindData in uiBindData.Where(d => d != null && !string.IsNullOrEmpty(d.Name)))
            {
                if (bindData.BindType == EBindType.ListCom)
                {
                    continue;
                }

                var firstType = bindData.GetFirstOrDefaultType();
                if (firstType == null)
                {
                    continue;
                }

                var config = FindEventConfig(firstType.FullName, eventConfigs);
                if (config == null)
                {
                    continue;
                }

                var publicName = publicNameFactory(bindData.Name);
                var eventName = GetEventFuncName(publicName, config);
                var signature = string.IsNullOrEmpty(config.CallbackSignature) ? "()" : config.CallbackSignature;

                handlers.AppendLine($"\t\tprivate partial void {eventName}{signature};");
            }

            return handlers.ToString();
        }

        private static UIEventBindingConfig FindEventConfig(string componentTypeFullName, List<UIEventBindingConfig> configs)
        {
            if (string.IsNullOrEmpty(componentTypeFullName))
            {
                return null;
            }

            return configs.FirstOrDefault(c =>
                string.Equals(c.ComponentType, componentTypeFullName, StringComparison.Ordinal));
        }

        private static string GetEventFuncName(string publicName, UIEventBindingConfig config)
        {
            var triggerName = config.TriggerName;
            return $"On{triggerName}{publicName}";
        }

        public string GetWindowContent(string className, string nameSpace, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            var eventHandlers = GenerateWindowEventHandlers(uiBindData, publicNameFactory);

            var content = new StringBuilder();
            content.AppendLine("using Moirai.Atropos.UI;");
            content.AppendLine("using UnityEngine;");
            content.AppendLine();
            content.AppendLine($"namespace {nameSpace}");
            content.AppendLine("{");
            content.AppendLine("    [Window(UILayer.UI)]");
            content.AppendLine($"    public partial class {className} : UIWindow");
            content.AppendLine("    {");

            if (!string.IsNullOrEmpty(eventHandlers))
            {
                content.AppendLine("        #region 事件 [EVENTS]");
                content.AppendLine();
                content.Append(eventHandlers);
                content.AppendLine("        #endregion");
            }

            content.AppendLine("    }");
            content.AppendLine("}");

            return content.ToString();
        }

        private static string GenerateWindowEventHandlers(IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            if (uiBindData == null || uiBindData.Count == 0)
            {
                return string.Empty;
            }

            var eventConfigs = UIGeneratorSettings.UIEventBindingConfigs;
            if (eventConfigs == null || eventConfigs.Count == 0)
            {
                return string.Empty;
            }

            var handlers = new StringBuilder();
            foreach (var bindData in uiBindData.Where(d => d != null && !string.IsNullOrEmpty(d.Name)))
            {
                if (bindData.BindType == EBindType.ListCom)
                {
                    continue;
                }

                var firstType = bindData.GetFirstOrDefaultType();
                if (firstType == null)
                {
                    continue;
                }

                var config = FindEventConfig(firstType.FullName, eventConfigs);
                if (config == null)
                {
                    continue;
                }

                var publicName = publicNameFactory(bindData.Name);
                var eventName = GetEventFuncName(publicName, config);
                var signature = string.IsNullOrEmpty(config.CallbackSignature) ? "()" : config.CallbackSignature;

                handlers.AppendLine($"        private partial void {eventName}{signature}");
                handlers.AppendLine("        {");
                handlers.AppendLine("        }");
            }

            return handlers.ToString();
        }
    }

    public sealed class DefaultUIScriptFileWriter : IUIScriptFileWriter
    {
        public void Write(GameObject targetObject, string className, string scriptContent, UIScriptGenerateData scriptGenerateData, string windowScriptContent = null)
        {
            if (string.IsNullOrEmpty(className)) throw new ArgumentNullException(nameof(className));
            if (scriptContent == null) throw new ArgumentNullException(nameof(scriptContent));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            var scriptFolderPath = scriptGenerateData.GenerateHolderCodePath;
            var scriptFilePath = Path.Combine(scriptFolderPath, $"{className}.g.cs");

            Directory.CreateDirectory(scriptFolderPath);

            #region 自动生成脚本

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
                File.WriteAllText(windowFilePath, windowScriptContent, Encoding.UTF8);
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