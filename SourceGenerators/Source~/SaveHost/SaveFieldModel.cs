using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>字段捕获分类（与 KVT 记录类型一一对应）。</summary>
    internal enum FieldKind
    {
        Unsupported,
        Bool,
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        String,
        DateTime,
        TimeSpan,
        Vector2,
        Vector3,
        Vector4,
        Quaternion,
        Color,
        Rect,
        Bounds,
        Enum,

        /// <summary>序列（数组/List/Queue/Stack/HashSet）。</summary>
        Sequence,

        /// <summary>映射（Dictionary）。</summary>
        Map,

        /// <summary>嵌套 [SaveData] 数据类。</summary>
        NestedObject,

        /// <summary>场景对象引用（GameObject/Component，存 SaveObjectIdentity 稳定 ID）。</summary>
        SceneReference,

        /// <summary>资产引用（Texture/SO/Material 等，存 SaveAssetCatalog 定位串）。</summary>
        AssetReference,
    }

    /// <summary>序列容器种类（恢复侧容器构建策略）。</summary>
    internal enum SequenceContainer
    {
        None,
        Array,
        List,
        Queue,
        Stack,
        HashSet,
    }

    /// <summary>
    /// 值类型模型（递归：集合元素/映射键值/嵌套对象字段各持一份子模型）。
    /// </summary>
    internal sealed class SaveValueModel
    {
        /// <summary>分类。</summary>
        public FieldKind Kind { get; set; }

        /// <summary>类型显示名（诊断消息用）。</summary>
        public string TypeDisplay { get; set; }

        /// <summary>类型全限定名（global:: 前缀，生成代码声明用）。</summary>
        public string TypeFqn { get; set; }

        /// <summary>枚举底层类型的写入/读取方法后缀（非枚举为 null）。</summary>
        public string EnumUnderlyingName { get; set; }

        /// <summary>枚举底层类型的 C# 类型名（cast 用；非枚举为 null）。</summary>
        public string EnumUnderlyingCsType { get; set; }

        /// <summary>序列容器种类（Sequence 有效）。</summary>
        public SequenceContainer Container { get; set; }

        /// <summary>序列元素模型（Sequence 有效）。</summary>
        public SaveValueModel Element { get; set; }

        /// <summary>映射键模型（Map 有效；仅标量/枚举）。</summary>
        public SaveValueModel Key { get; set; }

        /// <summary>映射值模型（Map 有效）。</summary>
        public SaveValueModel Value { get; set; }

        /// <summary>嵌套对象字段列表（NestedObject 有效；全部 public 实例字段）。</summary>
        public List<SaveNestedFieldModel> Fields { get; set; }

        /// <summary>场景引用目标为 GameObject（false = Component 派生；SceneReference 有效）。</summary>
        public bool IsGameObject { get; set; }

        /// <summary>分类期收集的诊断（仅顶层字段模型携带；嵌套成员问题归并到顶层字段位置报告）。</summary>
        public List<DiagnosticInfo> Diagnostics { get; set; }

        /// <summary>
        /// 生成写入侧 cast 表达式（枚举 → 底层类型）。
        /// </summary>
        /// <param name="expression">值表达式。</param>
        /// <returns>cast 后的表达式。</returns>
        public string CastToUnderlying(string expression)
        {
            return Kind == FieldKind.Enum ? $"({EnumUnderlyingCsType}){expression}" : expression;
        }

        /// <summary>
        /// 生成读取侧 cast 表达式（底层类型 → 枚举）。
        /// </summary>
        /// <param name="expression">值表达式。</param>
        /// <returns>cast 后的表达式。</returns>
        public string CastFromUnderlying(string expression)
        {
            return Kind == FieldKind.Enum ? $"({TypeFqn}){expression}" : expression;
        }
    }

    /// <summary>
    /// 嵌套对象字段模型（键 = 字段名，无显式键语义——对齐 JSON 序列化惯例）。
    /// </summary>
    internal sealed class SaveNestedFieldModel
    {
        /// <summary>存档键（= 字段名）。</summary>
        public string Key { get; set; }

        /// <summary>字段名。</summary>
        public string FieldName { get; set; }

        /// <summary>字段值模型。</summary>
        public SaveValueModel Value { get; set; }

        /// <summary>字段位置（诊断报告用）。</summary>
        public Location Location { get; set; }
    }

    /// <summary>嵌套链类型信息（自包含组件向外逐层）。</summary>
    internal sealed class ContainingTypeInfo
    {
        /// <summary>类型元数据名。</summary>
        public string Name { get; set; }

        /// <summary>是否声明 partial。</summary>
        public bool IsPartial { get; set; }

        /// <summary>是否为 class。</summary>
        public bool IsClass { get; set; }
    }

    /// <summary>
    /// <c>[SaveField]</c> 字段模型（字符串化快照，支撑增量缓存等价比较）。
    /// </summary>
    internal sealed class SaveFieldModel
    {
        /// <summary>包含类型的全限定名（global:: 前缀，可用于 typeof）。</summary>
        public string ContainingTypeFqn { get; private set; }

        /// <summary>包含类型的元数据名（用于 partial class 声明）。</summary>
        public string ContainingTypeName { get; private set; }

        /// <summary>包含类型的命名空间（空 = 全局）。</summary>
        public string ContainingNamespace { get; private set; }

        /// <summary>AddSource 提示名前缀（按命名空间消重）。</summary>
        public string HintPrefix { get; private set; }

        /// <summary>包含类型是否为 class。</summary>
        public bool IsClass { get; private set; }

        /// <summary>包含类型是否声明为 partial。</summary>
        public bool IsPartial { get; private set; }

        /// <summary>嵌套链（自包含组件向外逐层，仅含组件自身嵌套在其它类型内时的层级；顶层组件为空）。</summary>
        public List<ContainingTypeInfo> ContainingChain { get; private set; }

        /// <summary>包含类型是否派生自 MonoBehaviour（组件捕获器发射门槛；非组件类型仅作为嵌套数据参与）。</summary>
        public bool IsMonoBehaviour { get; private set; }

        /// <summary>字段是否为 static/const（实例字段校验用）。</summary>
        public bool IsStaticOrConst { get; private set; }

        /// <summary>存档键（显式指定或字段名）。</summary>
        public string Key { get; private set; }

        /// <summary>字段名。</summary>
        public string FieldName { get; private set; }

        /// <summary>字段类型显示名。</summary>
        public string TypeDisplay { get; private set; }

        /// <summary>字段值模型。</summary>
        public SaveValueModel Value { get; private set; }

        /// <summary>字段分类（Value.Kind 便捷转发）。</summary>
        public FieldKind Kind => Value.Kind;

        /// <summary>字段位置（诊断报告用）。</summary>
        public Location Location { get; private set; }

        /// <summary>包含类型的组件模式版本（[SaveComponentSchema] 声明，缺省 1）。</summary>
        public int SchemaVersion { get; private set; }

        /// <summary>SaveFieldAttribute 全名（特性匹配判定）。</summary>
        private const string SaveFieldAttributeName = "Moirai.Atropos.Save.SaveFieldAttribute";

        /// <summary>
        /// 从语法上下文创建字段模型（一个字段声明可含多个变量，逐一展开）。
        /// <para>注：不用 <c>ForAttributeWithMetadataName</c>——该增量 API 在本项目部分编译单元上静默不产出（实证），改用语义扫描。</para>
        /// </summary>
        /// <param name="context">语法提供上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>字段模型数组（无 [SaveField] 标注时为空）。</returns>
        public static SaveFieldModel[] Create(GeneratorSyntaxContext context, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = (FieldDeclarationSyntax)context.Node;
            List<SaveFieldModel> models = null;
            foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
            {
                if (context.SemanticModel.GetDeclaredSymbol(variable, cancellationToken) is not IFieldSymbol fieldSymbol)
                {
                    continue;
                }

                AttributeData saveFieldAttribute = null;
                foreach (AttributeData attribute in fieldSymbol.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == SaveFieldAttributeName)
                    {
                        saveFieldAttribute = attribute;
                        break;
                    }
                }

                if (saveFieldAttribute == null)
                {
                    continue;
                }

                SaveFieldModel model = CreateFromSymbol(fieldSymbol, saveFieldAttribute, context.SemanticModel.Compilation, cancellationToken);
                if (model != null)
                {
                    (models ??= new List<SaveFieldModel>()).Add(model);
                }
            }

            return models?.ToArray() ?? Array.Empty<SaveFieldModel>();
        }

        /// <summary>
        /// 从字段符号创建字段模型。
        /// </summary>
        /// <param name="fieldSymbol">字段符号。</param>
        /// <param name="saveFieldAttribute">[SaveField] 特性数据。</param>
        /// <param name="compilation">编译单元。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>字段模型（包含类型缺失时返回 null）。</returns>
        private static SaveFieldModel CreateFromSymbol(IFieldSymbol fieldSymbol, AttributeData saveFieldAttribute, Compilation compilation, System.Threading.CancellationToken cancellationToken)
        {
            if (fieldSymbol.ContainingType == null)
            {
                return null;
            }

            INamedTypeSymbol containingType = fieldSymbol.ContainingType;
            bool isPartial = false;
            foreach (SyntaxReference reference in containingType.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax typeDeclaration
                    && typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    isPartial = true;
                    break;
                }
            }

            string explicitKey = null;
            if (saveFieldAttribute.ConstructorArguments.Length > 0
                && saveFieldAttribute.ConstructorArguments[0].Value is string keyArgument)
            {
                explicitKey = keyArgument;
            }

            string typeFqn = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldNamespace = containingType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : containingType.ContainingNamespace.ToDisplayString();

            // 嵌套链（外层类型逐层收集，含 partial/class 判定——生成代码须逐层 partial 包裹以命中同一类型）
            List<ContainingTypeInfo> chain = null;
            var hintBuilder = new System.Text.StringBuilder(string.IsNullOrEmpty(fieldNamespace) ? string.Empty : fieldNamespace.Replace('.', '_') + ".");
            for (INamedTypeSymbol level = containingType; level != null; level = level.ContainingType)
            {
                if (!ReferenceEquals(level, containingType))
                {
                    bool levelPartial = false;
                    foreach (SyntaxReference levelReference in level.DeclaringSyntaxReferences)
                    {
                        if (levelReference.GetSyntax(cancellationToken) is TypeDeclarationSyntax levelDeclaration
                            && levelDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                        {
                            levelPartial = true;
                            break;
                        }
                    }

                    (chain ??= new List<ContainingTypeInfo>()).Add(new ContainingTypeInfo
                    {
                        Name = level.Name,
                        IsPartial = levelPartial,
                        IsClass = level.TypeKind == TypeKind.Class,
                    });
                    hintBuilder.Insert(hintBuilder.Length, level.Name + "_");
                }
            }

            string hintPrefix = hintBuilder.ToString();

            int schemaVersion = 1;
            foreach (AttributeData attribute in containingType.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == "Moirai.Atropos.Save.SaveComponentSchemaAttribute"
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is int declaredVersion)
                {
                    schemaVersion = declaredVersion < 1 ? 1 : declaredVersion;
                    break;
                }
            }

            var model = new SaveFieldModel
            {
                ContainingTypeFqn = typeFqn,
                ContainingTypeName = containingType.Name,
                ContainingNamespace = fieldNamespace,
                HintPrefix = hintPrefix,
                IsClass = containingType.TypeKind == TypeKind.Class,
                IsPartial = isPartial,
                ContainingChain = chain,
                IsMonoBehaviour = DerivesFrom(containingType, "UnityEngine.MonoBehaviour"),
                IsStaticOrConst = fieldSymbol.IsStatic || fieldSymbol.IsConst,
                Key = explicitKey ?? fieldSymbol.Name,
                FieldName = fieldSymbol.Name,
                TypeDisplay = fieldSymbol.Type.ToDisplayString(),
                Value = SaveValueClassifier.Classify(fieldSymbol.Type, compilation, containingType, fieldSymbol),
                Location = fieldSymbol.Locations.Length > 0 ? fieldSymbol.Locations[0] : Location.None,
                SchemaVersion = schemaVersion,
            };
            return model;
        }

        /// <summary>
        /// 类型是否派生自指定全名基类（含自身命中）。
        /// </summary>
        /// <param name="type">类型。</param>
        /// <param name="baseTypeName">基类全名。</param>
        /// <returns>命中返回 <c>true</c>。</returns>
        internal static bool DerivesFrom(ITypeSymbol type, string baseTypeName)
        {
            for (INamedTypeSymbol current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (current.ToDisplayString() == baseTypeName)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 字段/嵌套成员类型分类器（与 KVT 类型码对齐；v2 扩展集合/映射/嵌套数据类/UnityEngine.Object 引用）。
    /// </summary>
    internal static class SaveValueClassifier
    {
        /// <summary>SaveDataAttribute 全名（嵌套数据类标记）。</summary>
        private const string SaveDataAttributeName = "Moirai.Atropos.Save.SaveDataAttribute";

        /// <summary>SaveDataBlock 全名（手动轨数据块——嵌套场景拒绝，引导走手动轨）。</summary>
        private const string SaveDataBlockName = "Moirai.Atropos.Save.SaveDataBlock";

        /// <summary>
        /// 分类字段类型（写入/读取方法与 KVT 类型码对齐）。
        /// </summary>
        /// <param name="type">字段类型。</param>
        /// <param name="compilation">编译单元（可访问性判定）。</param>
        /// <param name="componentType">包含组件类型（生成代码的访问上下文）。</param>
        /// <param name="fieldSymbol">字段符号（诊断位置）。</param>
        /// <returns>值模型（含诊断信息挂接）。</returns>
        public static SaveValueModel Classify(ITypeSymbol type, Compilation compilation, INamedTypeSymbol componentType, IFieldSymbol fieldSymbol)
        {
            var diagnostics = new List<DiagnosticInfo>();
            var chain = new List<INamedTypeSymbol>();
            Location location = fieldSymbol.Locations.Length > 0 ? fieldSymbol.Locations[0] : Location.None;
            SaveValueModel model = ClassifyValue(type, compilation, componentType, diagnostics, chain, fieldSymbol.Name, location);
            model.Diagnostics = diagnostics;
            return model;
        }

        /// <summary>
        /// 递归分类核心。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="compilation">编译单元。</param>
        /// <param name="componentType">生成代码访问上下文。</param>
        /// <param name="diagnostics">诊断收集器。</param>
        /// <param name="chain">嵌套数据类递归链（循环引用检测）。</param>
        /// <param name="memberPath">成员路径（诊断消息用，如 <c>_data.Kills</c>）。</param>
        /// <param name="location">诊断落点（顶层字段位置）。</param>
        private static SaveValueModel ClassifyValue(ITypeSymbol type, Compilation compilation, INamedTypeSymbol componentType,
            List<DiagnosticInfo> diagnostics, List<INamedTypeSymbol> chain, string memberPath, Location location)
        {
            var model = new SaveValueModel
            {
                Kind = FieldKind.Unsupported,
                TypeDisplay = type.ToDisplayString(),
                TypeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: model.Kind = FieldKind.Bool; return model;
                case SpecialType.System_SByte: model.Kind = FieldKind.SByte; return model;
                case SpecialType.System_Byte: model.Kind = FieldKind.Byte; return model;
                case SpecialType.System_Int16: model.Kind = FieldKind.Int16; return model;
                case SpecialType.System_UInt16: model.Kind = FieldKind.UInt16; return model;
                case SpecialType.System_Int32: model.Kind = FieldKind.Int32; return model;
                case SpecialType.System_UInt32: model.Kind = FieldKind.UInt32; return model;
                case SpecialType.System_Int64: model.Kind = FieldKind.Int64; return model;
                case SpecialType.System_UInt64: model.Kind = FieldKind.UInt64; return model;
                case SpecialType.System_Single: model.Kind = FieldKind.Single; return model;
                case SpecialType.System_Double: model.Kind = FieldKind.Double; return model;
                case SpecialType.System_Decimal: model.Kind = FieldKind.Decimal; return model;
                case SpecialType.System_Char: model.Kind = FieldKind.Char; return model;
                case SpecialType.System_String: model.Kind = FieldKind.String; return model;
                default:
                    break;
            }

            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumSymbol && enumSymbol.EnumUnderlyingType != null)
            {
                (model.EnumUnderlyingName, model.EnumUnderlyingCsType) = enumSymbol.EnumUnderlyingType.SpecialType switch
                {
                    SpecialType.System_SByte => ("SByte", "sbyte"),
                    SpecialType.System_Byte => ("Byte", "byte"),
                    SpecialType.System_Int16 => ("Int16", "short"),
                    SpecialType.System_UInt16 => ("UInt16", "ushort"),
                    SpecialType.System_Int64 => ("Int64", "long"),
                    SpecialType.System_UInt64 => ("UInt64", "ulong"),
                    SpecialType.System_Int32 => ("Int32", "int"),
                    SpecialType.System_UInt32 => ("UInt32", "uint"),
                    _ => ("Int32", "int"),
                };
                model.Kind = FieldKind.Enum;
                return model;
            }

            switch (type.ToDisplayString())
            {
                case "System.DateTime": model.Kind = FieldKind.DateTime; return model;
                case "System.TimeSpan": model.Kind = FieldKind.TimeSpan; return model;
                case "UnityEngine.Vector2": model.Kind = FieldKind.Vector2; return model;
                case "UnityEngine.Vector3": model.Kind = FieldKind.Vector3; return model;
                case "UnityEngine.Vector4": model.Kind = FieldKind.Vector4; return model;
                case "UnityEngine.Quaternion": model.Kind = FieldKind.Quaternion; return model;
                case "UnityEngine.Color": model.Kind = FieldKind.Color; return model;
                case "UnityEngine.Rect": model.Kind = FieldKind.Rect; return model;
                case "UnityEngine.Bounds": model.Kind = FieldKind.Bounds; return model;
                default:
                    break;
            }

            // 数组 → 序列
            if (type is IArrayTypeSymbol arrayType)
            {
                model.Kind = FieldKind.Sequence;
                model.Container = SequenceContainer.Array;
                model.Element = ClassifyElement(arrayType.ElementType, compilation, componentType, diagnostics, chain, memberPath, location);
                return model;
            }

            // 泛型集合 → 序列/映射
            if (type is INamedTypeSymbol genericType && genericType.IsGenericType)
            {
                string originalDefinition = genericType.OriginalDefinition.ToDisplayString();
                switch (originalDefinition)
                {
                    case "System.Collections.Generic.List<T>":
                        model.Kind = FieldKind.Sequence;
                        model.Container = SequenceContainer.List;
                        model.Element = ClassifyElement(genericType.TypeArguments[0], compilation, componentType, diagnostics, chain, memberPath, location);
                        return model;
                    case "System.Collections.Generic.Queue<T>":
                        model.Kind = FieldKind.Sequence;
                        model.Container = SequenceContainer.Queue;
                        model.Element = ClassifyElement(genericType.TypeArguments[0], compilation, componentType, diagnostics, chain, memberPath, location);
                        return model;
                    case "System.Collections.Generic.Stack<T>":
                        model.Kind = FieldKind.Sequence;
                        model.Container = SequenceContainer.Stack;
                        model.Element = ClassifyElement(genericType.TypeArguments[0], compilation, componentType, diagnostics, chain, memberPath, location);
                        return model;
                    case "System.Collections.Generic.HashSet<T>":
                        model.Kind = FieldKind.Sequence;
                        model.Container = SequenceContainer.HashSet;
                        model.Element = ClassifyElement(genericType.TypeArguments[0], compilation, componentType, diagnostics, chain, memberPath, location);
                        return model;
                    case "System.Collections.Generic.Dictionary<TKey, TValue>":
                    {
                        model.Kind = FieldKind.Map;
                        model.Key = ClassifyValue(genericType.TypeArguments[0], compilation, componentType, diagnostics, chain, memberPath + ".key", location);
                        if (!IsScalarOrEnum(model.Key.Kind))
                        {
                            diagnostics.Add(DiagnosticInfo.UnsupportedMember(memberPath + ".key", model.Key.TypeDisplay,
                                "映射键仅支持标量/枚举类型", location));
                            model.Kind = FieldKind.Unsupported;
                            return model;
                        }

                        model.Value = ClassifyValue(genericType.TypeArguments[1], compilation, componentType, diagnostics, chain, memberPath + ".value", location);
                        if (model.Value.Kind == FieldKind.SceneReference || model.Value.Kind == FieldKind.AssetReference)
                        {
                            diagnostics.Add(DiagnosticInfo.UnsupportedMember(memberPath + ".value", model.Value.TypeDisplay,
                                "映射值暂不支持引用类型（引用字段仅支持为直接字段）", location));
                            model.Kind = FieldKind.Unsupported;
                            return model;
                        }

                        return model;
                    }
                    default:
                        break;
                }
            }

            // UnityEngine.Object 家族 → 场景/资产引用
            if (SaveFieldModel.DerivesFrom(type, "UnityEngine.Object"))
            {
                if (type.ToDisplayString() == "UnityEngine.Object")
                {
                    diagnostics.Add(DiagnosticInfo.AmbiguousReference(memberPath, location));
                    model.Kind = FieldKind.Unsupported;
                    return model;
                }

                if (type.ToDisplayString() == "UnityEngine.GameObject")
                {
                    model.Kind = FieldKind.SceneReference;
                    model.IsGameObject = true;
                    return model;
                }

                if (SaveFieldModel.DerivesFrom(type, "UnityEngine.Component"))
                {
                    model.Kind = FieldKind.SceneReference;
                    model.IsGameObject = false;
                    return model;
                }

                model.Kind = FieldKind.AssetReference;
                return model;
            }

            // 嵌套 [SaveData] 数据类
            if (type is INamedTypeSymbol nestedType && HasSaveDataAttribute(nestedType))
            {
                return ClassifyNestedObject(nestedType, compilation, componentType, diagnostics, chain, memberPath, location, model);
            }

            return model;
        }

        /// <summary>
        /// 分类序列元素（引用元素暂不支持——引用字段仅支持为直接字段）。
        /// </summary>
        private static SaveValueModel ClassifyElement(ITypeSymbol elementType, Compilation compilation, INamedTypeSymbol componentType,
            List<DiagnosticInfo> diagnostics, List<INamedTypeSymbol> chain, string memberPath, Location location)
        {
            SaveValueModel element = ClassifyValue(elementType, compilation, componentType, diagnostics, chain, memberPath + "[]", location);
            if (element.Kind == FieldKind.SceneReference || element.Kind == FieldKind.AssetReference)
            {
                diagnostics.Add(DiagnosticInfo.UnsupportedMember(memberPath + "[]", element.TypeDisplay,
                    "集合元素暂不支持引用类型（引用字段仅支持为直接字段）", location));
                element.Kind = FieldKind.Unsupported;
            }

            return element;
        }

        /// <summary>
        /// 分类嵌套 [SaveData] 数据类（全部 public 实例字段递归捕获）。
        /// </summary>
        private static SaveValueModel ClassifyNestedObject(INamedTypeSymbol nestedType, Compilation compilation, INamedTypeSymbol componentType,
            List<DiagnosticInfo> diagnostics, List<INamedTypeSymbol> chain, string memberPath, Location location, SaveValueModel model)
        {
            if (nestedType.TypeKind != TypeKind.Class)
            {
                diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(), "嵌套数据类型须为 class", location));
                return model;
            }

            if (nestedType.IsAbstract)
            {
                diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(), "抽象类无法实例化恢复", location));
                return model;
            }

            if (SaveFieldModel.DerivesFrom(nestedType, SaveDataBlockName))
            {
                diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(),
                    "SaveDataBlock 子类为手动轨数据块，不能作为嵌套字段类型（改用手动轨块 API）", location));
                return model;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(chain[i], nestedType))
                {
                    diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(), "嵌套数据类型循环引用", location));
                    return model;
                }
            }

            if (!compilation.IsSymbolAccessibleWithin(nestedType, componentType))
            {
                diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(), "类型对生成代码不可访问", location));
                return model;
            }

            if (!HasAccessibleParameterlessConstructor(nestedType, compilation, componentType))
            {
                diagnostics.Add(DiagnosticInfo.InvalidNested(memberPath, nestedType.ToDisplayString(), "缺少可访问的无参构造函数（null 恢复时无法实例化）", location));
                return model;
            }

            model.Kind = FieldKind.NestedObject;
            model.Fields = new List<SaveNestedFieldModel>();
            chain.Add(nestedType);
            foreach (ISymbol member in nestedType.GetMembers())
            {
                if (member is not IFieldSymbol memberField
                    || memberField.IsStatic || memberField.IsConst || memberField.IsImplicitlyDeclared
                    || memberField.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                SaveValueModel memberValue = ClassifyValue(memberField.Type, compilation, componentType, diagnostics, chain, memberPath + "." + memberField.Name, location);
                model.Fields.Add(new SaveNestedFieldModel
                {
                    Key = memberField.Name,
                    FieldName = memberField.Name,
                    Value = memberValue,
                    Location = memberField.Locations.Length > 0 ? memberField.Locations[0] : Location.None,
                });
            }

            chain.RemoveAt(chain.Count - 1);
            return model;
        }

        /// <summary>
        /// 标量或枚举分类判定（映射键合法集）。
        /// </summary>
        private static bool IsScalarOrEnum(FieldKind kind)
        {
            return kind != FieldKind.Unsupported
                && kind != FieldKind.Sequence
                && kind != FieldKind.Map
                && kind != FieldKind.NestedObject
                && kind != FieldKind.SceneReference
                && kind != FieldKind.AssetReference;
        }

        /// <summary>
        /// 类型是否标注 [SaveData]。
        /// </summary>
        private static bool HasSaveDataAttribute(INamedTypeSymbol type)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == SaveDataAttributeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 是否具有生成代码可访问的无参构造（隐式默认构造或可访问的显式无参构造）。
        /// </summary>
        private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol classSymbol, Compilation compilation, INamedTypeSymbol within)
        {
            if (classSymbol.InstanceConstructors.Length == 0)
            {
                return true;
            }

            foreach (IMethodSymbol constructor in classSymbol.InstanceConstructors)
            {
                if (constructor.Parameters.Length == 0 && compilation.IsSymbolAccessibleWithin(constructor, within))
                {
                    return true;
                }
            }

            return false;
        }
    }

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

        /// <summary>创建不支持成员诊断（MIRAI308——集合元素/映射键值等嵌套位置）。</summary>
        public static DiagnosticInfo UnsupportedMember(string memberPath, string typeDisplay, string reason, Location location)
        {
            return new DiagnosticInfo(Diagnostics.UnsupportedNestedMember, location, new object[] { memberPath, typeDisplay, reason });
        }

        /// <summary>创建引用类型不明诊断（MIRAI306）。</summary>
        public static DiagnosticInfo AmbiguousReference(string memberPath, Location location)
        {
            return new DiagnosticInfo(Diagnostics.AmbiguousReferenceType, location, new object[] { memberPath });
        }

        /// <summary>创建嵌套数据类型无效诊断（MIRAI307）。</summary>
        public static DiagnosticInfo InvalidNested(string memberPath, string typeDisplay, string reason, Location location)
        {
            return new DiagnosticInfo(Diagnostics.InvalidNestedType, location, new object[] { memberPath, typeDisplay, reason });
        }
    }

    /// <summary>
    /// SaveHost 诊断描述符（MIRAI3xx 系列，与 ServiceDependencyAnalyzer 的 MIRAI1xx/2xx 系列错开）。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>诊断类别。</summary>
        private const string Category = "SaveHost";

        /// <summary>MIRAI300：字段类型不受生成器支持。</summary>
        public static readonly DiagnosticDescriptor UnsupportedFieldType = new(
            "MIRAI300",
            "SaveField 字段类型不受支持",
            "[SaveField] 字段 '{0}.{1}' 的类型 '{2}' 不受 SaveHost 生成器支持（支持：基元/枚举/string/DateTime/TimeSpan/Unity 数学类型、数组/List/Queue/Stack/HashSet/Dictionary、嵌套 [SaveData] 类、UnityEngine.Object 引用）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI301：存档键重复。</summary>
        public static readonly DiagnosticDescriptor DuplicateKey = new(
            "MIRAI301",
            "SaveField 存档键重复",
            "类型 '{0}' 内存在重复的存档键 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI302：ISaveMigrator 实现无法生成自注册代码。</summary>
        public static readonly DiagnosticDescriptor InvalidMigrator = new(
            "MIRAI302",
            "ISaveMigrator 实现无法自注册",
            "ISaveMigrator 实现 '{0}' 无法生成自注册代码：须为具体（非抽象）类、不能嵌套在私有类型内、且提供可访问的无参构造函数",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI303：包含类型必须为 partial class。</summary>
        public static readonly DiagnosticDescriptor TypeMustBePartial = new(
            "MIRAI303",
            "包含类型必须为 partial class",
            "[SaveField] 所在类型 '{0}' 必须声明为 partial class（嵌套组件的外层类型链每一级同样须为 partial class——生成捕获器逐层包裹以访问字段）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI304：字段必须为实例字段。</summary>
        public static readonly DiagnosticDescriptor MustBeInstanceField = new(
            "MIRAI304",
            "SaveField 必须标注实例字段",
            "[SaveField] 不能标注类型 '{0}' 的静态/常量字段 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI305：场景引用字段指引（目标须挂 SaveObjectIdentity）。</summary>
        public static readonly DiagnosticDescriptor SceneReferenceGuidance = new(
            "MIRAI305",
            "场景引用字段需 SaveObjectIdentity",
            "[SaveField] 场景引用字段 '{0}'：捕获存目标 SaveObjectIdentity 的稳定 ID——被引用物体须挂 SaveObjectIdentity 组件，否则捕获写 Null",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        /// <summary>MIRAI306：引用字段类型不明（UnityEngine.Object 基类无法区分场景/资产）。</summary>
        public static readonly DiagnosticDescriptor AmbiguousReferenceType = new(
            "MIRAI306",
            "引用字段类型不明",
            "[SaveField] 引用成员 '{0}' 声明为 UnityEngine.Object 基类，生成器无法区分场景引用与资产引用——请使用具体类型（Component 派生/GameObject = 场景引用；Texture/ScriptableObject 等 = 资产引用），该字段不参与捕获",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>MIRAI307：嵌套数据类型无效。</summary>
        public static readonly DiagnosticDescriptor InvalidNestedType = new(
            "MIRAI307",
            "嵌套数据类型无效",
            "[SaveField] 嵌套数据成员 '{0}' 的类型 '{1}' 无效：{2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI308：集合元素/映射键值成员类型不受支持。</summary>
        public static readonly DiagnosticDescriptor UnsupportedNestedMember = new(
            "MIRAI308",
            "集合/嵌套成员类型不受支持",
            "[SaveField] 成员 '{0}' 的类型 '{1}' 不受支持：{2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
