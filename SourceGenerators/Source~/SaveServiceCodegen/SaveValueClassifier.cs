using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
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
}
