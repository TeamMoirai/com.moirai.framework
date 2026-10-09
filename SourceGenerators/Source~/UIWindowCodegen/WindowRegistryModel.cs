using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 窗口登记模型：带 <c>[Window]</c> 的窗口类的符号级提取与校验结果。
    /// </summary>
    internal sealed class WindowRegistryModel
    {
        /// <summary>窗口类符号。</summary>
        public INamedTypeSymbol Type { get; private set; }

        /// <summary>类型反射全名（嵌套类带 <c>+</c>），作缺省窗口名。</summary>
        public string ReflectionFullName { get; private set; }

        /// <summary>特性解析出的描述符取值（注册期即定，运行期零解析）。</summary>
        public int WindowLayer { get; private set; }
        public bool FromResources { get; private set; }
        public string Location { get; private set; }
        public bool FullScreen { get; private set; }
        public byte Modal { get; private set; }
        public int HideTimeToClose { get; private set; }
        public float CacheTimeToDestroy { get; private set; }

        /// <summary>不可登记的成因诊断；可登记时为 null。</summary>
        public Diagnostic? InvalidReason { get; private set; }

        private WindowRegistryModel(INamedTypeSymbol type)
        {
            Type = type;
            ReflectionFullName = BuildReflectionFullName(type);
            Location = type.Name;
            WindowLayer = DefaultLayer;
            HideTimeToClose = DefaultHideTimeToClose;
        }

        private const int DefaultLayer = 1;          // UILayer.UI
        private const int DefaultHideTimeToClose = 10;
        private const byte DefaultModal = 0;         // EUIModal.Inherit

        /// <summary>
        /// 从语法上下文提取模型：非 [Window] 类回 null；[Window] 窗口类回带取值或成因诊断的模型。
        /// </summary>
        /// <param name="ctx">语法提供器上下文。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>候选模型；与本生成器无关时为 null。</returns>
        public static WindowRegistryModel? Create(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            var declaration = (ClassDeclarationSyntax)ctx.Node;
            if (ctx.SemanticModel.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol symbol)
            {
                return null;
            }

            AttributeData? attribute = FindWindowAttribute(symbol);
            if (attribute == null)
            {
                return null;
            }

            var model = new WindowRegistryModel(symbol);
            if (IsWindow(symbol))
            {
                model.Decode(attribute);
            }
            else
            {
                model.InvalidReason = Diagnostic.Create(Diagnostics.WindowAttributeOnNonWindow,
                    symbol.Locations.Length > 0 ? symbol.Locations[0] : Microsoft.CodeAnalysis.Location.None,
                    symbol.ToDisplayString());
            }

            return model;
        }

        /// <summary>判定是否 UIWindow 派生类（沿基类链找 UIBase 之下的窗口模型）。</summary>
        private static bool IsWindow(INamedTypeSymbol symbol)
        {
            return FindWindowBase(symbol) != null;
        }

        private static INamedTypeSymbol? FindWindowBase(INamedTypeSymbol symbol)
        {
            for (var current = symbol.BaseType; current != null; current = current.BaseType)
            {
                if (current.ToDisplayString() == "Moirai.Atropos.UI.UIWindow")
                {
                    return current;
                }
            }

            return null;
        }

        private static AttributeData? FindWindowAttribute(INamedTypeSymbol symbol)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "Moirai.Atropos.UI.WindowAttribute")
                {
                    return attribute;
                }
            }

            return null;
        }

        /// <summary>
        /// 解码 <c>[Window]</c> 特性实参：位置实参按特性自己的构造器形参表对号，命名实参随后覆盖。
        /// </summary>
        /// <remarks>
        /// 形参表是唯一的形状真源：构造器增减形参时这里不必跟着改走位，也不会把值落进邻档（实测过按位置猜形状会把 <c>modal</c> 读成 <c>hideTimeToClose</c> 的槽位且零诊断）。<br />
        /// 命名实参的键按大小写不敏感匹配形参名与字段名，两种键形态（形参名 / PascalCase 字段名）都落同一档。
        /// </remarks>
        private void Decode(AttributeData attribute)
        {
            ImmutableArray<IParameterSymbol> parameters = attribute.AttributeConstructor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty;
            ImmutableArray<TypedConstant> args = attribute.ConstructorArguments;
            for (var i = 0; i < args.Length && i < parameters.Length; i++)
            {
                Apply(parameters[i].Name, args[i]);
            }

            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                Apply(named.Key, named.Value);
            }

            if (string.IsNullOrEmpty(Location))
            {
                Location = Type.Name;
            }
        }

        /// <summary>把一枚实参落进它自己名下那一档；名字对不上形参表时不动任何档。</summary>
        /// <param name="name">形参名或字段名。</param>
        /// <param name="value">该名的实参。</param>
        private void Apply(string name, TypedConstant value)
        {
            switch (name.ToLowerInvariant())
            {
                case "windowlayer":
                    WindowLayer = ToInt32(value.Value, DefaultLayer);
                    break;
                case "location":
                    Location = ToStringValue(value.Value, Type.Name);
                    break;
                case "fromresources":
                    FromResources = ToBool(value.Value);
                    break;
                case "fullscreen":
                    FullScreen = ToBool(value.Value);
                    break;
                case "hidetimetoclose":
                    HideTimeToClose = ToInt32(value.Value, DefaultHideTimeToClose);
                    break;
                case "modal":
                    Modal = ToByte(value.Value, DefaultModal);
                    break;
                case "cachetimetodestroy":
                    CacheTimeToDestroy = ToSingle(value.Value, CacheTimeToDestroy);
                    break;
            }
        }

        private static int ToInt32(object? constant, int fallback)
        {
            switch (constant)
            {
                case byte v: return v;
                case sbyte v: return v;
                case short v: return v;
                case ushort v: return v;
                case int v: return v;
                case uint v: return checked((int)v);
                case long v: return checked((int)v);
                case ulong v: return checked((int)v);
                default: return fallback;
            }
        }

        private static bool ToBool(object? constant) => constant is bool value && value;
        private static string ToStringValue(object? constant, string fallback) => constant as string ?? fallback;

        /// <remarks>NaN/±Infinity 是合法的编译期特性实参，但发射侧按 "R" 拼字面量会得到 <c>NaNf</c>/<c>Infinityf</c>（整工程编不过），故非有限值回 fallback。</remarks>
        private static float ToSingle(object? constant, float fallback)
        {
            var value = constant is float f ? f : constant is double d ? (float)d : constant is int n ? n : fallback;
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        /// <summary>枚举实参折算 byte（三态模态等小型枚举；值越界回 fallback）。</summary>
        private static byte ToByte(object? constant, byte fallback)
        {
            switch (constant)
            {
                case byte v: return v;
                case sbyte v when v >= 0: return (byte)v;
                case int v when v is >= 0 and <= byte.MaxValue: return (byte)v;
                default: return fallback;
            }
        }

        /// <summary>反射全名：命名空间 + 嵌套链（<c>.</c> 与 <c>+</c> 按反射口径拼）。</summary>
        private static string BuildReflectionFullName(INamedTypeSymbol symbol)
        {
            var parts = new Stack<string>();
            for (var current = symbol; current != null; current = current.ContainingType)
            {
                parts.Push(current.Name);
            }

            var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } nsSymbol
                ? nsSymbol.ToDisplayString()
                : string.Empty;

            return ns.Length == 0
                ? string.Join("+", parts)
                : ns + "." + string.Join("+", parts);
        }
    }
}
