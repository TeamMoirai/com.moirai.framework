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
    /// 序列化属性（<see cref="SerializedProperty"/>）相关工具类。
    /// </summary>
    public static class SerializedUtils
    {
        /// <summary>
        /// 通过自动实现属性的 C# 属性名查找其后备字段对应的序列化属性。
        /// </summary>
        /// <param name="obj">要在其上查找的序列化对象。</param>
        /// <param name="propName">自动实现属性的属性名。</param>
        /// <returns>后备字段对应的序列化属性；不存在时为 null。</returns>
        public static SerializedProperty FindPropertyByAutoPropertyName(SerializedObject obj, string propName)
        {
            return obj.FindProperty($"<{propName}>k__BackingField");
        }

        /// <summary>
        /// 在当前序列化属性的子级中，通过自动实现属性的 C# 属性名查找其后备字段对应的序列化属性。
        /// </summary>
        /// <param name="property">要在其子级中查找的序列化属性。</param>
        /// <param name="propName">自动实现属性的属性名。</param>
        /// <returns>后备字段对应的序列化属性；不存在时为 null。</returns>
        public static SerializedProperty FindPropertyByAutoPropertyName(SerializedProperty property, string propName)
        {
            return property.FindPropertyRelative($"<{propName}>k__BackingField");
        }

        /// <summary>
        /// 表示一次按名称查找成员的结果，成员可能是字段或属性。
        /// </summary>
        public struct FieldOrProp
        {
            /// <summary>成员是否为字段。</summary>
            public bool IsField;
            /// <summary>字段信息；成员不是字段时为 null。</summary>
            public FieldInfo FieldInfo;
            /// <summary>属性信息；成员不是属性时为 null。</summary>
            public PropertyInfo PropertyInfo;
        }

        /// <summary>
        /// 解析序列化属性路径，返回其对应成员（字段或属性）的信息及该成员的直接宿主对象；
        /// 路径中含数组/列表元素段时逐级下钻到对应元素。
        /// </summary>
        /// <param name="property">待解析的序列化属性。</param>
        /// <returns>返回一个元组：fieldOrProp 为成员信息，parent 为成员的直接宿主对象；解析失败时两者均为默认值/null。</returns>
        public static (FieldOrProp fieldOrProp, object parent) GetFieldInfoAndDirectParent(SerializedProperty property)
        {
            string originPath = property.propertyPath;
            string[] propPaths = originPath.Split('.');
            (bool arrayTrim, IEnumerable<string> propPathSegments) = TrimEndArray(propPaths);
            if (arrayTrim)
            {
                propPaths = propPathSegments.ToArray();
            }

            object sourceObj = property.serializedObject.targetObject;
            FieldOrProp fieldOrProp = default;

            bool preNameIsArray = false;
            foreach (int propIndex in Enumerable.Range(0, propPaths.Length))
            {
                string propSegName = propPaths[propIndex];
                // Debug.Log($"check key {propSegName}");
                if(propSegName == "Array")
                {
                    preNameIsArray = true;
                    continue;
                }
                if (propSegName.StartsWith("data[") && propSegName.EndsWith("]"))
                {
                    Debug.Assert(preNameIsArray);
                    // Debug.Log(propSegName);
                    // Debug.Assert(targetProp != null);
                    preNameIsArray = false;

                    int elemIndex = Convert.ToInt32(propSegName.Substring(5, propSegName.Length - 6));

                    object useObject;

                    if(fieldOrProp.FieldInfo is null && fieldOrProp.PropertyInfo is null)
                    {
                        useObject = sourceObj;
                    }
                    else
                    {
                        useObject = fieldOrProp.IsField
                            // ReSharper disable once PossibleNullReferenceException
                            ? fieldOrProp.FieldInfo.GetValue(sourceObj)
                            : fieldOrProp.PropertyInfo.GetValue(sourceObj);
                    }

                    // Debug.Log($"Get index from obj {useObject}[{elemIndex}]");
                    sourceObj = Util.GetValueAtIndex(useObject, elemIndex).Item2;
                    // Debug.Log($"Get index from obj `{useObject}` returns {sourceObj}");
                    fieldOrProp = default;
                    // Debug.Log($"[index={elemIndex}]={targetObj}");
                    continue;
                }

                preNameIsArray = false;

                // if (propSegName.StartsWith("<") && propSegName.EndsWith(">k__BackingField"))
                // {
                //     propSegName = propSegName.Substring(1, propSegName.Length - 17);
                // }

                // Debug.Log($"get obj {sourceObj}.{propSegName}")
                //
                if (sourceObj == null)  // TODO: 需要更完善的错误处理
                {
                    return (default, null);
                }
                // ;
                if (!(fieldOrProp.FieldInfo is null) || !(fieldOrProp.PropertyInfo is null))
                {
                    sourceObj = fieldOrProp.IsField
                        // ReSharper disable once PossibleNullReferenceException
                        ? fieldOrProp.FieldInfo.GetValue(sourceObj)
                        : fieldOrProp.PropertyInfo.GetValue(sourceObj);
                    // Debug.Log($"get key {propSegName} sourceObj = {sourceObj}");
                }

                fieldOrProp = GetFileOrProp(sourceObj, propSegName);
                // Debug.Log($"get key {propSegName} => {(fieldOrProp.IsField ? fieldOrProp.FieldInfo.Name : fieldOrProp.PropertyInfo.Name)}");
                // targetFieldName = propSegName;
                // Debug.Log($"[{propSegName}]={targetObj}");
            }

            return (fieldOrProp, sourceObj);
        }

        /// <summary>
        /// 获取序列化属性所属的数组/列表序列化属性。
        /// </summary>
        /// <param name="property">数组/列表元素的序列化属性。</param>
        /// <returns>返回一个元组：error 为错误描述（成功时为空字符串），property 为所属数组/列表的序列化属性（失败时为 null）。</returns>
        public static (string error, SerializedProperty property) GetArrayProperty(SerializedProperty property)
        {
            // Debug.Log(property.propertyPath);
            string[] paths = property.propertyPath.Split('.');

            (bool arrayTrim, IEnumerable<string> propPathSegments) = TrimEndArray(paths);
            if (!arrayTrim)
            {
                return ($"{property.propertyPath} is not an array/list.", null);
            }

            string arrayPath = string.Join(".", propPathSegments);
            SerializedProperty arrayProp = property.serializedObject.FindProperty(arrayPath);
            if (arrayProp == null)
            {
                return ($"Can't find {arrayPath} on {property.serializedObject}", null);
            }

            if (!arrayProp.isArray)
            {
                return ($"{arrayPath} on {property.serializedObject} is not an array/list", null);
            }

            return ("", arrayProp);
        }

        /// <summary>
        /// 判断属性路径末尾是否为数组/列表元素段（Array.data[i]），若是则移除这两段并返回剩余路径。
        /// </summary>
        /// <param name="propPathSegments">按「.」拆分后的属性路径段列表。</param>
        /// <returns>返回一个元组：trimed 表示是否发生了截取，propPathSegs 为处理后的路径段。</returns>
        private static (bool trimed, IEnumerable<string> propPathSegs) TrimEndArray(IReadOnlyList<string> propPathSegments)
        {

            int usePathLength = propPathSegments.Count;

            if (usePathLength <= 2)
            {
                return (false, propPathSegments);
            }

            string lastPart = propPathSegments[usePathLength - 1];
            string secLastPart = propPathSegments[usePathLength - 2];
            bool isArray = secLastPart == "Array" && lastPart.StartsWith("data[") && lastPart.EndsWith("]");
            if (!isArray)
            {
                return (false, propPathSegments);
            }

            // 旧版 Unity 没有 SkipLast 方法
            List<string> propPaths = new List<string>(propPathSegments);
            propPaths.RemoveAt(propPaths.Count - 1);
            propPaths.RemoveAt(propPaths.Count - 1);
            return (true, propPaths);
        }

        /// <summary>
        /// 获取序列化属性对应成员上指定类型的所有特性，以及该成员的直接宿主对象。
        /// </summary>
        /// <typeparam name="T">要获取的特性类型。</typeparam>
        /// <param name="property">待解析的序列化属性。</param>
        /// <returns>返回一个元组：attributes 为成员上匹配 <typeparamref name="T"/> 的特性数组，parent 为成员的直接宿主对象。</returns>
        public static (T[] attributes, object parent) GetAttributesAndDirectParent<T>(SerializedProperty property) where T : class
        {
            (FieldOrProp fieldOrProp, object sourceObj) = GetFieldInfoAndDirectParent(property);
            // Debug.Log(fieldOrProp.IsField);
            // Debug.Log(fieldOrProp.PropertyInfo);
            // Debug.Log(fieldOrProp.PropertyInfo.GetCustomAttributes());
            // 此方式不适用于接口类型
            // Debug.Log(fieldOrProp.FieldInfo.GetCustomAttributes(typeof(ISaintsAttribute)));
            // Debug.Log(fieldOrProp.FieldInfo.GetCustomAttributes());
            T[] attributes = fieldOrProp.IsField
                ? fieldOrProp.FieldInfo.GetCustomAttributes()
                    .OfType<T>()
                    .ToArray()
                : fieldOrProp.PropertyInfo.GetCustomAttributes()
                    .OfType<T>()
                    .ToArray();
            return (attributes, sourceObj);
        }

        /// <summary>
        /// 在对象类型及其基类链上按名称查找字段或属性。
        /// </summary>
        /// <param name="source">成员所属的宿主对象。</param>
        /// <param name="name">成员名称。</param>
        /// <returns>查找到的成员信息。</returns>
        /// <exception cref="Exception">整个继承链上均未找到该名称的成员时抛出。</exception>
        private static FieldOrProp GetFileOrProp(object source, string name)
        {
            Type type = source.GetType();
            // Debug.Log($"get type {type}");

            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    // Debug.Log($"return field {field.Name} by {name}");
                    return new FieldOrProp
                    {
                        IsField = true,
                        PropertyInfo = null,
                        FieldInfo = field,
                    };
                }

                PropertyInfo property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    // return property.GetValue(source, null);
                    // Debug.Log($"return prop {property.Name} by {name}");
                    return new FieldOrProp
                    {
                        IsField = false,
                        PropertyInfo = property,
                        FieldInfo = null,
                    };
                }

                type = type.BaseType;
            }

            throw new Exception($"Unable to get type from {source}");
        }

        /// <summary>
        /// 解析序列化属性路径末尾的数组/列表元素索引。
        /// </summary>
        /// <param name="propertyPath">序列化属性路径。</param>
        /// <returns>路径末尾为 <c>data[i]</c> 时返回元素索引 <c>i</c>；否则返回 -1。</returns>
        public static int PropertyPathIndex(string propertyPath)
        {
            string[] propPaths = propertyPath.Split('.');
            // ReSharper disable once UseIndexFromEndExpression
            string lastPropPath = propPaths[propPaths.Length - 1];
            if (lastPropPath.StartsWith("data[") && lastPropPath.EndsWith("]"))
            {
                return int.Parse(lastPropPath.Substring(5, lastPropPath.Length - 6));
            }

            return -1;
        }

        /// <summary>
        /// 枚举序列化属性的所有可见直接子级（不含孙级及更深层次）。
        /// </summary>
        /// <param name="property">父序列化属性。</param>
        /// <returns>直接子级序列化属性的枚举。</returns>
        public static IEnumerable<SerializedProperty> GetPropertyChildren(SerializedProperty property)
        {
            if (property == null || string.IsNullOrEmpty(property.propertyPath))
            {
                yield break;
            }

            // ReSharper disable once ConvertToUsingDeclaration
            using (SerializedProperty iterator = property.Copy())
            {
                if (!iterator.NextVisible(true))
                {
                    yield break;
                }

                do
                {
                    SerializedProperty childProperty = property.FindPropertyRelative(iterator.name);
                    yield return childProperty;
                } while (iterator.NextVisible(false));
            }
        }

        /// <summary>
        /// 获取序列化属性的唯一标识，由目标对象实例 ID 与属性路径拼接而成。
        /// </summary>
        /// <param name="property">目标序列化属性。</param>
        /// <returns>格式为「实例ID.属性路径」的唯一字符串。</returns>
        public static string GetUniqueId(SerializedProperty property)
        {
            return $"{property.serializedObject.targetObject.GetInstanceID()}.{property.propertyPath}";
        }

    }
}
