using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
    }

    /// <summary>
    /// 默认脚本代码生成器实现
    /// </summary>
    public sealed class DefaultUIScriptCodeEmitter : IUIScriptCodeEmitter
    {
        private const string BINDER_VARIABLE_NAME = "_bindComponent";

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
            controllerContent.AppendLine($"\t\t\tif({BINDER_VARIABLE_NAME} == null)");
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
                controllerContent.AppendLine("\t\t#region 事件 [EVENTS]");
                controllerContent.AppendLine(eventHandlers);
                controllerContent.AppendLine("\t\t#endregion");
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

            return configs.FirstOrDefault(c =>
                string.Equals(c.ComponentType, componentTypeFullName, StringComparison.Ordinal));
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
                content.AppendLine("\t\t#region 事件 [EVENTS]");
                content.AppendLine();
                content.Append(eventHandlers);
                content.AppendLine("\t#endregion");
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
                handlers.AppendLine("\t{");
                handlers.AppendLine("\t}");
            }

            return handlers.ToString();
        }
    }
}