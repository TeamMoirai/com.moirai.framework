using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    public static class Util
    {
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
                            // ignore
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
                        if (r)  // life circle problem, need to check bool first
                        {
                            result = r;
                        }
                    }
                    break;

                // Unity Build-in Object
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