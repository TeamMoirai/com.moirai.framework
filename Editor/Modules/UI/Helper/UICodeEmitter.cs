using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// 脚本代码生成器接口，定义UI脚本代码的生成方法
    /// </summary>
    public interface IUIScriptCodeEmitter
    {
        /// <summary>
        /// 获取引用的命名空间
        /// </summary>
        string GetReferenceNamespaces(List<UIBindData> uiBindData);

        /// <summary>
        /// 获取变量声明内容
        /// </summary>
        string GetVariableContent(List<UIBindData> uiBindData, Func<string, string> publicNameFactory);

        /// <summary>
        /// 获取Controller部分代码内容
        /// </summary>
        string GetControllerContent(string className, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory);

        /// <summary>
        /// 获取窗口实现类代码内容
        /// </summary>
        string GetWindowContent(string className, string nameSpace, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory);

        /// <summary>
        /// 将新生成的窗口内容中缺失的事件方法补充到已存在的窗口实现类内容中
        /// </summary>
        string PatchWindowContent(string existingContent, string newWindowContent);
    }

    /// <summary>
    /// 默认脚本代码生成器实现
    /// </summary>
    public sealed class DefaultUIScriptCodeEmitter : IUIScriptCodeEmitter
    {
        private const string EVENTS_REGION_START = "#region 事件 [EVENTS]";
        private const string EVENTS_REGION_END = "#endregion 事件 [EVENTS]";

        private const string BINDER_VARIABLE_NAME = "_binder";

        private static readonly Regex s_MethodSignatureRegex = new Regex(
            @"private\s+partial\s+void\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public string GetControllerContent(string className, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            var propertyAccessors = GeneratePropertyAccessors(uiBindData, publicNameFactory);
            var eventSubscriptions = GenerateEventSubscriptions(uiBindData, publicNameFactory);
            var eventHandlers = GenerateEventHandlers(uiBindData, publicNameFactory);

            var controllerContent = new StringBuilder();
            controllerContent.AppendLine();
            controllerContent.AppendLine($"\tpublic partial class {className}");
            controllerContent.AppendLine("\t{");
            controllerContent.AppendLine($"\t\tprivate {className}Binder {BINDER_VARIABLE_NAME};");

            if (!string.IsNullOrEmpty(propertyAccessors))
            {
                controllerContent.AppendLine();
                controllerContent.Append(propertyAccessors);
            }

            controllerContent.AppendLine();
            controllerContent.AppendLine("\t\tprotected override void ScriptGenerator()");
            controllerContent.AppendLine("\t\t{");
            controllerContent.AppendLine($"\t\t\t{BINDER_VARIABLE_NAME} = gameObject.GetComponent<{className}Binder>();");
            controllerContent.AppendLine($"\t\t\tif ({BINDER_VARIABLE_NAME} == null)");
            controllerContent.AppendLine("\t\t\t{");
            controllerContent.AppendLine($"\t\t\t\tLog.Error($\"根物体: {{gameObject.name}} 缺少组件 {className}Binder, 请检查！！！\");");
            controllerContent.AppendLine("\t\t\t\treturn;");
            controllerContent.AppendLine("\t\t\t}");

            if (!string.IsNullOrEmpty(eventSubscriptions))
            {
                controllerContent.AppendLine();
                controllerContent.Append(eventSubscriptions);
            }

            controllerContent.AppendLine("\t\t}");

            if (!string.IsNullOrEmpty(eventHandlers))
            {
                controllerContent.AppendLine();
                controllerContent.AppendLine($"\t\t{EVENTS_REGION_START}");
                controllerContent.AppendLine();
                controllerContent.Append(eventHandlers);
                controllerContent.AppendLine();
                controllerContent.AppendLine($"\t\t{EVENTS_REGION_END}");
            }

            controllerContent.Append("\t}");

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

            // 先尝试精确匹配
            var exactMatch = configs.FirstOrDefault(c =>
                string.Equals(c.ComponentType, componentTypeFullName, StringComparison.Ordinal));
            if (exactMatch != null)
            {
                return exactMatch;
            }

            // 尝试继承匹配：检查配置的类型是否是实际类型的基类
            var actualType = AssemblyUtility.GetType(componentTypeFullName);
            if (actualType == null)
            {
                return null;
            }

            return configs.FirstOrDefault(c =>
            {
                var configType = AssemblyUtility.GetType(c.ComponentType);
                return configType != null && configType.IsAssignableFrom(actualType);
            });
        }

        private static string GetEventFuncName(string publicName, UIEventBindingConfig config)
        {
            var triggerName = config.TriggerName;
            return $"On{triggerName}{publicName}";
        }

        /// <inheritdoc/>
        public string GetWindowContent(string className, string nameSpace, IReadOnlyList<UIBindData> uiBindData, Func<string, string> publicNameFactory)
        {
            var eventHandlers = GenerateWindowEventHandlers(uiBindData, publicNameFactory);

            var content = new StringBuilder();
            content.AppendLine("using Moirai.Atropos.UI;");
            content.AppendLine("using UnityEngine;");
            content.AppendLine();
            content.AppendLine($"namespace {nameSpace}");
            content.AppendLine("{");
            content.AppendLine("\t[Window(UILayer.UI)]");
            content.AppendLine($"\tpublic partial class {className} : UIWindow");
            content.AppendLine("\t{");

            if (!string.IsNullOrEmpty(eventHandlers))
            {
                content.AppendLine($"\t\t{EVENTS_REGION_START}");
                content.AppendLine();
                content.Append(eventHandlers);
                content.AppendLine();
                content.AppendLine($"\t\t{EVENTS_REGION_END}");
            }

            content.AppendLine("\t}");
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

                handlers.AppendLine($"\t\tprivate partial void {eventName}{signature}");
                handlers.AppendLine("\t\t{");
                handlers.AppendLine("\t\t\t// do something");
                handlers.AppendLine("\t\t}");
            }

            return handlers.ToString();
        }

        /// <inheritdoc/>
        public string PatchWindowContent(string existingContent, string newWindowContent)
        {
            var existingMethodNames = ExtractMethodNames(existingContent);
            var newMethodSignatures = ExtractMethodSignatures(newWindowContent);

            var missingStubs = new List<string>();
            foreach (var (methodName, signatureLine) in newMethodSignatures)
            {
                if (existingMethodNames.Contains(methodName))
                {
                    continue;
                }

                var indent = DetectIndent(existingContent);
                missingStubs.Add($"{indent}{signatureLine}\n{indent}{{\n{indent}\t// do something\n{indent}}}");
            }

            if (missingStubs.Count == 0)
            {
                return existingContent;
            }

            var missingContent = string.Join(Environment.NewLine, missingStubs);
            return InsertMissingMethods(existingContent, missingContent);
        }

        private static HashSet<string> ExtractMethodNames(string content)
        {
            var names = new HashSet<string>();
            foreach (Match match in s_MethodSignatureRegex.Matches(content))
            {
                names.Add(match.Groups[1].Value);
            }
            return names;
        }

        private static List<(string methodName, string signatureLine)> ExtractMethodSignatures(string content)
        {
            var result = new List<(string, string)>();
            var regionStartIdx = content.IndexOf(EVENTS_REGION_START, StringComparison.Ordinal);
            if (regionStartIdx < 0) return result;

            var regionEndIdx = content.IndexOf(EVENTS_REGION_END, regionStartIdx + EVENTS_REGION_START.Length, StringComparison.Ordinal);
            if (regionEndIdx < 0) return result;

            var regionContent = content.Substring(regionStartIdx, regionEndIdx - regionStartIdx);

            foreach (Match match in s_MethodSignatureRegex.Matches(regionContent))
            {
                result.Add((match.Groups[1].Value, match.Value));
            }

            return result;
        }

        private static string DetectIndent(string content)
        {
            var regionIdx = content.IndexOf(EVENTS_REGION_START, StringComparison.Ordinal);
            if (regionIdx >= 0)
            {
                var lineStart = content.LastIndexOf('\n', regionIdx);
                var line = lineStart >= 0 ? content.Substring(lineStart + 1, regionIdx - lineStart - 1) : content.Substring(0, regionIdx);
                var indent = line.TrimEnd('\r');
                if (indent.Length > 0) return indent;
            }

            return "\t\t";
        }

        private static string InsertMissingMethods(string existingContent, string missingMethods)
        {
            var regionStartIdx = existingContent.IndexOf(EVENTS_REGION_START, StringComparison.Ordinal);
            if (regionStartIdx >= 0)
            {
                var regionEndIdx = existingContent.IndexOf(EVENTS_REGION_END, regionStartIdx + EVENTS_REGION_START.Length, StringComparison.Ordinal);
                if (regionEndIdx >= 0)
                {
                    var lineStart = existingContent.LastIndexOf('\n', regionEndIdx);
                    var insertIdx = lineStart >= 0 ? lineStart + 1 : 0;
                    return existingContent.Substring(0, insertIdx) + missingMethods + Environment.NewLine + Environment.NewLine + existingContent.Substring(insertIdx);
                }
            }

            var lastBrace = existingContent.LastIndexOf('}');
            if (lastBrace > 0)
            {
                var secondLastBrace = existingContent.LastIndexOf('}', lastBrace - 1);
                var insertPos = secondLastBrace >= 0 ? secondLastBrace : lastBrace;

                // 定位到插入点所在行的行首，保留该行的缩进
                var lineStart = existingContent.LastIndexOf('\n', insertPos);
                var insertIdx = lineStart >= 0 ? lineStart + 1 : 0;

                var indent = DetectIndent(existingContent);
                var eventsSection = Environment.NewLine + indent + EVENTS_REGION_START + Environment.NewLine + Environment.NewLine
                    + missingMethods + Environment.NewLine
                    + indent + EVENTS_REGION_END + Environment.NewLine;
                return existingContent.Substring(0, insertIdx) + eventsSection + existingContent.Substring(insertIdx);
            }

            return existingContent + Environment.NewLine + missingMethods;
        }
    }
}