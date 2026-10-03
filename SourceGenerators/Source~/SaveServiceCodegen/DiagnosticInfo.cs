using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 分类期诊断信息（位置 + 描述符 + 消息参数，延迟到生成期落诊断）。
    /// </summary>
    internal sealed class DiagnosticInfo
    {
        public DiagnosticDescriptor Descriptor { get; private set; }
        public Location Location { get; private set; }
        public object[] Args { get; private set; }

        private DiagnosticInfo(DiagnosticDescriptor descriptor, Location location, object[] args)
        {
            Descriptor = descriptor;
            Location = location;
            Args = args;
        }

        /// <summary>
        /// 创建不支持成员诊断（MIRAI308——集合元素/映射键值等嵌套位置）。
        /// </summary>
        public static DiagnosticInfo UnsupportedMember(string memberPath, string typeDisplay, string reason, Location location)
        {
            return new DiagnosticInfo(Diagnostics.UnsupportedNestedMember, location, new object[] { memberPath, typeDisplay, reason });
        }

        /// <summary>
        /// 创建引用类型不明诊断（MIRAI306）。
        /// </summary>
        public static DiagnosticInfo AmbiguousReference(string memberPath, Location location)
        {
            return new DiagnosticInfo(Diagnostics.AmbiguousReferenceType, location, new object[] { memberPath });
        }

        /// <summary>
        /// 创建嵌套数据类型无效诊断（MIRAI307）。
        /// </summary>
        public static DiagnosticInfo InvalidNested(string memberPath, string typeDisplay, string reason, Location location)
        {
            return new DiagnosticInfo(Diagnostics.InvalidNestedType, location, new object[] { memberPath, typeDisplay, reason });
        }
    }
}
