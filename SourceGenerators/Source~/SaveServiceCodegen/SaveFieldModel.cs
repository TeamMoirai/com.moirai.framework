using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 字段捕获分类（与 KVT 记录类型一一对应）。
    /// </summary>
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

    /// <summary>
    /// 序列容器种类（恢复侧容器构建策略）。
    /// </summary>
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

    /// <summary>
    /// 嵌套链类型信息（自包含组件向外逐层）。
    /// </summary>
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
        /// </summary>
        /// <remarks>
        /// 采用语义扫描而非 <c>ForAttributeWithMetadataName</c>（该增量 API 在本项目部分编译单元上静默不产出）。
        /// </remarks>
        /// <param name="context">语法提供上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>字段模型数组（无 <c>[SaveField]</c> 标注时为空）。</returns>
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
}
