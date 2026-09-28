using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    /// <summary>
    /// 反射工具类，提供成员查找、真值判断、参数填充与通过反射写入对象值等能力。
    /// </summary>
    public static class ReflectUtils
    {
        /// <summary>
        /// 获取目标对象的自身类型及其全部基类类型，按从最基类到自身类型的顺序排列。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <returns>包含自身类型及所有基类类型的列表。</returns>
        public static List<Type> GetSelfAndBaseTypes(object target)
        {
            List<Type> types = new List<Type>
            {
                target.GetType(),
            };

            while (types.Last().BaseType != null)
            {
                types.Add(types.Last().BaseType);
            }

            types.Reverse();

            return types;
        }

        /// <summary>
        /// 表示 <see cref="ReflectUtils.GetProp"/> 按名称查找成员的结果类型。
        /// </summary>
        public enum GetPropType
        {
            /// <summary>未找到匹配成员。</summary>
            NotFound,
            /// <summary>匹配到属性（<see cref="PropertyInfo"/>）。</summary>
            Property,
            /// <summary>匹配到字段（<see cref="FieldInfo"/>）。</summary>
            Field,
            /// <summary>匹配到方法（<see cref="MethodInfo"/>）。</summary>
            Method,
        }

        /// <summary>
        /// 在目标类型上按名称查找成员，查找顺序为字段（含自动属性的后备字段）、属性、方法。
        /// </summary>
        /// <param name="targetType">要查找的目标类型。</param>
        /// <param name="fieldName">成员名称。</param>
        /// <returns>返回一个元组：getPropType 为成员类型，fieldOrMethodInfo 为对应的 <see cref="FieldInfo"/>、<see cref="PropertyInfo"/> 或 <see cref="MethodInfo"/>，未找到时为 null。</returns>
        public static (GetPropType getPropType, object fieldOrMethodInfo) GetProp(Type targetType, string fieldName)
        {
            const BindingFlags bindAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic |
                                          BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.FlattenHierarchy;

            FieldInfo fieldInfo = targetType.GetField(fieldName, bindAttr);
            // Debug.Log($"init get fieldInfo {fieldInfo}");
            if (fieldInfo == null)
            {
                fieldInfo = targetType.GetField($"<{fieldName}>k__BackingField", bindAttr);
            }
            if (fieldInfo != null)
            {
                return (GetPropType.Field, fieldInfo);
            }

            PropertyInfo propertyInfo = targetType.GetProperty(fieldName, bindAttr);
            if (propertyInfo != null)
            {
                return (GetPropType.Property, propertyInfo);
            }

            MethodInfo methodInfo = targetType.GetMethod(fieldName, bindAttr);
            // Debug.Log($"methodInfo={methodInfo}, fieldName={fieldName}, targetType={targetType}/FlattenHierarchy={bindAttr.HasFlag(BindingFlags.FlattenHierarchy)}");
            return methodInfo == null ? (GetPropType.NotFound, null) : (GetPropType.Method, methodInfo);

        }

        /// <summary>
        /// 以宽松规则判断给定值是否等价于 true：null 与空字符串返回 false；可转换为布尔类型时按其布尔值判断；
        /// 其余情况尝试转换为 <see cref="UnityEngine.Object"/> 判断（Unity 假 null 视为 false）；仍无法转换时视为 true。
        /// </summary>
        /// <param name="value">待判断的值。</param>
        /// <returns>值等价于 true 返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool Truly(object value)
        {
            if (value is string stringValue)
            {
                return stringValue != "";
            }

            try
            {
                // Debug.Log($"try convert to bool");
                return Convert.ToBoolean(value);
            }
            catch (InvalidCastException)
            {
                bool equalNull = value == null;
                if (equalNull)
                {
                    // Debug.Log($"InvalidCastException, but value is null.");
                    return false;
                }
                try
                {
                    // Debug.Log($"try to cast to UnityEngine.Object");
                    return (UnityEngine.Object)value != null;
                }
                catch (InvalidCastException)
                {
                    // Debug.Log($"failed to cast to UnityEngine.Object");
                    return true;
                }
            }
            catch (NullReferenceException)
            {
                // Debug.Log($"Null, return false");
                return false;
            }
        }

        /// <summary>
        /// 方法参数填充过程中的中间记录。
        /// </summary>
        private class MethodParamFiller
        {
            /// <summary>参数名称。</summary>
            public string Name;
            /// <summary>是否为可选参数。</summary>
            public bool IsOptional;
            /// <summary>可选参数的默认值。</summary>
            public object DefaultValue;

            /// <summary>是否已填充实际值。</summary>
            public bool Signed;
            /// <summary>已填充的参数值。</summary>
            public object Value;
        }

        /// <summary>
        /// 将一组待填充值按顺序匹配填充到方法参数列表中：先填满必填参数，再将剩余值按类型匹配填充到可选参数。
        /// </summary>
        /// <param name="methodParams">目标方法的参数信息列表。</param>
        /// <param name="toFillValues">待填充的值序列。</param>
        /// <returns>与 <paramref name="methodParams"/> 顺序对应的参数值数组；未匹配到值的可选参数使用其默认值。</returns>
        public static object[] MethodParamsFill(IReadOnlyList<ParameterInfo> methodParams, IEnumerable<object> toFillValues)
        {
            // 第一步：先为各参数标记默认值与空值
            MethodParamFiller[] filledValues = methodParams
                .Select(param => param.IsOptional
                    ? new MethodParamFiller
                    {
                        Name = param.Name,
                        IsOptional = true,
                        DefaultValue = param.DefaultValue,
                    }
                    : new MethodParamFiller
                    {
                        Name = param.Name,
                    })
                .ToArray();
            // 第二步：逐一检查每个参数：
            // 1. 若存在必填参数，则为其填充值
            // 2. 接着，若仍有剩余待填充的值且类型能匹配可选参数，则继续填充
            // 3. 确保所有必填参数均已填充
            // 4. 返回结果。

            Queue<object> toFillQueue = new Queue<object>(toFillValues);
            Queue<object> leftOverQueue = new Queue<object>();
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
            Debug.Log($"toFillQueue.Count={toFillQueue.Count}");
#endif
            // 必填参数：
            foreach (int index in Enumerable.Range(0, methodParams.Count))
            {
                if (!methodParams[index].IsOptional)
                {
                    // Debug.Log($"checking {index}={methodParams[index].Name}");
                    Debug.Assert(toFillQueue.Count > 0, $"Nothing to fill required parameter {methodParams[index].Name}");
                    while(toFillQueue.Count > 0)
                    {
                        object value = toFillQueue.Dequeue();
                        Type paramType = methodParams[index].ParameterType;
                        if (value == null || paramType.IsInstanceOfType(value))
                        {
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
                            Debug.Log($"Push value {value} for {methodParams[index].Name}");
#endif
                            filledValues[index].Value = value;
                            filledValues[index].Signed = true;
                            break;
                        }
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
                        Debug.Log($"No push value {value}({value.GetType()}) for {methodParams[index].Name}({paramType})");
#endif

                        // Debug.Log($"Skip value {value} for {methodParams[index].Name}");
                        leftOverQueue.Enqueue(value);
                        // Debug.Assert(valueType == paramType || valueType.IsSubclassOf(paramType),
                        //     $"The value type `{valueType}` is not match the param type `{paramType}`");
                        // Debug.Log($"Add {value} at {index}");

                    }
                }
            }

            foreach (object leftOver in toFillQueue)
            {
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
                Debug.Log($"leftOver: {leftOver}");
#endif
                leftOverQueue.Enqueue(leftOver);
            }

            // 可选参数：
            if(leftOverQueue.Count > 0)
            {
                foreach (int index in Enumerable.Range(0, methodParams.Count))
                {
                    if (leftOverQueue.Count == 0)
                    {
                        break;
                    }

                    if (methodParams[index].IsOptional)
                    {
                        object value = leftOverQueue.Peek();
                        Type paramType = methodParams[index].ParameterType;
                        if(value == null || paramType.IsInstanceOfType(value))
                        {
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
                            Debug.Log($"add optional: {value} -> {methodParams[index].Name}({paramType})");
#endif
                            leftOverQueue.Dequeue();
                            filledValues[index].Value = value;
                            filledValues[index].Signed = true;
                        }
#if ATTRIBUTES_DEBUG && ATTRIBUTES_DEBUG_CALLBACK
                        else
                        {
                            Debug.Log($"not fit optional: {value}({value.GetType()}) -> {paramType}");
                        }
#endif
                    }
                }
            }

            return filledValues.Select(each =>
            {
                if (each.Signed)
                {
                    return each.Value;
                }
                Debug.Assert(each.IsOptional, $"No value for required parameter `{each.Name}` in method.");
                return each.DefaultValue;
            }).ToArray();
        }


        /// <summary>
        /// 通过反射将值写入目标对象的成员，并记录撤销操作；当序列化属性路径中带有索引时写入数组或列表的对应元素。
        /// </summary>
        /// <param name="propertyPath">序列化属性路径，用于解析数组/列表元素索引（-1 表示非集合元素）。</param>
        /// <param name="targetObject">目标对象，用于调用 <see cref="Undo.RecordObject"/> 以支持撤销。</param>
        /// <param name="info">成员信息（字段或属性）。</param>
        /// <param name="parent">成员所属的宿主对象。</param>
        /// <param name="value">要写入的值。</param>
        public static void SetValue(string propertyPath, UnityEngine.Object targetObject, MemberInfo info, object parent, object value)
        {
            Undo.RecordObject(targetObject, "SetValue");
            int index = SerializedUtils.PropertyPathIndex(propertyPath);
            if (index == -1)
            {
                if (info.MemberType == MemberTypes.Field)
                {
                    ((FieldInfo)info).SetValue(parent, value);
                }
                else if (info.MemberType == MemberTypes.Property)
                {
                    ((PropertyInfo)info).SetValue(parent, value);
                }
            }
            else
            {
                object fieldValue;
                if (info.MemberType == MemberTypes.Field)
                {
                    fieldValue = ((FieldInfo)info).GetValue(parent);
                }
                else if (info.MemberType == MemberTypes.Property)
                {
                    fieldValue = ((PropertyInfo)info).GetValue(parent);
                }
                else
                {
                    return;
                }

                // Debug.Log($"try set value {value} at {index} to {info} on {parent}");
                if (fieldValue is Array array)
                {
                    array.SetValue(value, index);
                }
                else if(fieldValue is IList list)
                {
                    list[index] = value;
                }
                else
                {
                    // Debug.Log($"direct set value {value} to {info} on {parent}");
                    // info.SetValue(parent, value);
                    if (info.MemberType == MemberTypes.Field)
                    {
                        ((FieldInfo)info).SetValue(parent, value);
                    }
                    else if (info.MemberType == MemberTypes.Property)
                    {
                        ((PropertyInfo)info).SetValue(parent, value);
                    }
                }
            }

        }

        /// <summary>
        /// 获取数组或泛型集合的元素类型；非集合类型则原样返回。
        /// </summary>
        /// <param name="type">待解析的类型。</param>
        /// <returns>数组或实现了 <see cref="IEnumerable"/> 的泛型集合的元素类型；否则返回 <paramref name="type"/> 本身。</returns>
        public static Type GetElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            {
                return type.GetGenericArguments()[0];
            }

            // if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            // {
            //     return type.GetGenericArguments()[0];
            // }

            return type;
        }

        /// <summary>
        /// 沿继承链向上查找，返回泛型继承链中最顶层的类型（其基类为 null 或非泛型）。
        /// </summary>
        /// <param name="type">起始类型。</param>
        /// <returns>泛型继承链上最基础的类型。</returns>
        public static Type GetMostBaseType(Type type)
        {
            Type lastType = type;
            while (true)
            {
                Type baseType = lastType.BaseType;
                if (baseType == null)
                {
                    return lastType;
                }

                if (!baseType.IsGenericType)
                {
                    return lastType;
                }

                lastType = baseType;
            }
        }

        /// <summary>
        /// 获取类型所实现的字典泛型接口（<see cref="IDictionary{TKey,TValue}"/> 或 <see cref="IReadOnlyDictionary{TKey,TValue}"/>）。
        /// </summary>
        /// <param name="type">待解析的类型。</param>
        /// <returns>匹配到的字典接口类型；未实现时返回 null。</returns>
        public static Type GetDictionaryType(Type type)
        {
            // 字典接口（IDictionary）
            return type
                .GetInterfaces()
                .FirstOrDefault(interfaceType =>
                    interfaceType.IsGenericType
                    && (
                        interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                        || interfaceType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                    )
                );
        }

        /// <summary>
        /// 判断指定类型是否为某个原始泛型类型定义（未指定类型参数）的子类。
        /// </summary>
        /// <param name="generic">泛型类型定义。</param>
        /// <param name="toCheck">待检查的类型。</param>
        /// <returns>是原始泛型类型定义的子类返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool IsSubclassOfRawGeneric(Type generic, Type toCheck) {
            while (toCheck != null && toCheck != typeof(object)) {
                Type cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur) {
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }
    }
}
