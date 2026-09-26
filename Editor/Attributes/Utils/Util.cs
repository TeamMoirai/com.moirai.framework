using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    /// <summary>
    /// 编辑器通用工具类。
    /// </summary>
    public static class Util
    {
        /// <summary>
        /// 按目标类型从对象中解析出可赋值的目标对象：GameObject 与 Component 之间按需转换（取 <c>gameObject</c> 或
        /// <c>GetComponent</c>），Texture2D 可解析为其所属资源中的 Sprite，其余情况要求对象实例与目标类型兼容。
        /// </summary>
        /// <param name="fieldResult">序列化属性当前引用的对象。</param>
        /// <param name="fieldType">期望的目标类型。</param>
        /// <returns>解析出的对象；无法解析或 <paramref name="fieldResult"/> 为 null 时返回 null。</returns>
        public static UnityEngine.Object GetTypeFromObj(UnityEngine.Object fieldResult, Type fieldType)
        {
            UnityEngine.Object result = null;
            switch (fieldResult)
            {
                case null:
                    // property.objectReferenceValue = null;
                    break;
                case ScriptableObject so:
                    // result = fieldType.IsSubclassOf(typeof())
                {
                    if (fieldType.IsInstanceOfType(so))
                    {
                        result = so;
                    }
                }
                    break;
                case GameObject go:
                    // ReSharper disable once RedundantCast
                    if (fieldType == typeof(GameObject) || fieldType.IsInstanceOfType(go))
                    {
                        result = go;
                    }
                    else
                    {
                        Component r = null;
                        try
                        {
                            r = go.GetComponent(fieldType);
                        }
                        catch (ArgumentException)
                        {
                            // 忽略
                        }

                        if (r)
                        {
                            result = r;
                        }
                    }

                    // Debug.Log($"isGo={fieldType == typeof(GameObject)},  fieldResult={fieldResult.GetType()} result={result.GetType()}");
                    break;
                case Component comp:
                    if (fieldType == typeof(GameObject) || fieldType.IsSubclassOf(typeof(GameObject)))
                    {
                        result = comp.gameObject;
                    }
                    else
                    {
                        Component r = comp.GetComponent(fieldType);
                        if (r)  // 存在生命周期问题，需先做 bool 判断
                        {
                            result = r;
                        }
                    }
                    break;

                // Unity 内置对象
                // case Texture:
                // case Sprite:
                // case Material:
                // case Mesh:
                // case Motion:
                // case AudioClip:
                //     result = fieldResult;
                //     break;
                case Texture2D _:
                {
                    if (fieldType == typeof(Sprite) || fieldType.IsSubclassOf(typeof(Sprite)))
                    {
                        string assetPath = AssetDatabase.GetAssetPath(fieldResult);
                        if(assetPath != "") {
                            result = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                        }

                        if (result == null)
                        {
                            goto default;
                        }
                    }
                    else
                    {
                        goto default;
                    }
                }
                    break;

                default:
                    // Debug.Log($"{fieldType}/{fieldResult}: {fieldType.IsInstanceOfType(fieldResult)}");
                    if (fieldType.IsInstanceOfType(fieldResult))
                    {
                        result = fieldResult;
                    }

                    break;
                //     Debug.Log(fieldResult.GetType());
                //     break;
            }

            return result;
        }
        
        /// <summary>
        /// 从可枚举对象中按索引取值，索引越界或未找到时返回错误信息而不抛出异常。
        /// </summary>
        /// <param name="source">源可枚举对象（数组、列表或任意 <see cref="IEnumerable"/>）。</param>
        /// <param name="index">要取出的元素索引。</param>
        /// <returns>返回一个元组：error 为错误描述（成功时为空字符串），result 为取到的元素（失败时为 null）。</returns>
        /// <exception cref="Exception"><paramref name="source"/> 不是可枚举对象时抛出。</exception>
        /// <summary>
        /// 从可枚举对象中按索引取值：数组/列表按索引访问并捕获越界异常，其余可枚举对象按顺序遍历查找。
        /// </summary>
        /// <param name="source">源可枚举对象。</param>
        /// <param name="index">元素索引。</param>
        /// <returns>返回一个元组：error 为错误描述（成功时为空字符串），result 为取到的元素（失败时为 null）。</returns>
        /// <exception cref="Exception"><paramref name="source"/> 不是可枚举对象时抛出。</exception>
        public static (string error, object result) GetValueAtIndex(object source, int index)
        {
            // ReSharper disable once UseNegatedPatternInIsExpression
            if (!(source is IEnumerable enumerable))
            {
                throw new Exception($"Not a enumerable {source}");
            }

            if (source is Array arr)
            {
                object result;
                try
                {
                    result = arr.GetValue(index);
                }
                catch (IndexOutOfRangeException e)
                {
                    return (e.Message, null);
                }

                return ("", result);
            }
            if (source is IList list)
            {
                object result;
                try
                {
                    result = list[index];
                }
                catch (ArgumentOutOfRangeException e)
                {
                    return (e.Message, null);
                }

                return ("", result);
            }

            // Debug.Log($"start check index in {source}");
            foreach ((object result, int searchIndex) in enumerable.Cast<object>().WithIndex())
            {
                // Debug.Log($"check index {searchIndex} in {source}");
                if(searchIndex == index)
                {
                    return ("", result);
                }
            }

            return ($"Not found index {index} in {source}", null);
        }

    }
}