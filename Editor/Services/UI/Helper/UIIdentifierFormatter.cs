using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// 标识符格式化器接口，定义变量名和类名的格式化规则
    /// </summary>
    public interface IUIIdentifierFormatter
    {
        /// <summary>
        /// 生成私有组件字段名
        /// </summary>
        string GetPrivateComponentName(string regexName, string componentName, EBindType bindType);

        /// <summary>
        /// 生成公共属性名
        /// </summary>
        string GetPublicComponentName(string variableName);

        /// <summary>
        /// 生成类名
        /// </summary>
        string GetClassName(GameObject targetObject);
    }

    /// <summary>
    /// 默认标识符格式化器实现
    /// </summary>
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

        /// <inheritdoc/>
        public string GetPrivateComponentName(string regexName, string componentName, EBindType bindType)
        {
            var endPrefix = bindType == EBindType.ListCom ? "List" : string.Empty;
            var endNameIndex = componentName.IndexOf(UIGeneratorSettings.ComCheckEndName, StringComparison.Ordinal);
            var componentSuffix = endNameIndex >= 0 ? componentName.Substring(endNameIndex + 1) : componentName;
            var fieldName = $"m_{NormalizeIdentifier(regexName)}{NormalizeIdentifier(componentSuffix)}{endPrefix}";
            return MakeSafeIdentifier(string.IsNullOrWhiteSpace(fieldName) ? "m_Component" : fieldName);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public string GetClassName(GameObject targetObject)
        {
            var objectName = NormalizeIdentifier(targetObject.name);
            var className = $"{objectName}";
            return MakeSafeIdentifier(string.IsNullOrEmpty(className) ? "View" : className);
        }

        /// <summary>
        /// 规范化标识符，移除非法字符并拼接
        /// </summary>
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

        /// <summary>
        /// 确保标识符安全，处理数字开头和C#关键字
        /// </summary>
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
}