using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Events;

namespace Moirai.Atropos
{
    // =====================================================================
    // 本文件是全部 JsonHandler 的【通用属性标识契约】：
    //
    // • DefaultJsonHandler（DefaultJson）   — 完整支持：序列化/反序列化名称解析
    //   （JsonSerializeAs 重命名、FormerlySerializedAs 旧名兼容）、Include/Exclude、
    //   序列化前/反序列化后回调，语义见 DefaultJson.ReflectionCache。
    // • NewtonsoftJsonHandler              — 经 CustomContractResolver 支持
    //   JsonPropertyAttribute（重命名/读写开关）；回调特性不生效（Newtonsoft 有自己的
    //   OnSerializing/OnDeserialized 回调机制）。
    //
    // 各 handler 共享 <see cref="JsonUtility.TypeIsForbidden"/> 的类型排除契约
    // （UnityEngine.Object 派生与 UnityEvent 一律不序列化）。
    // =====================================================================

    /// <summary>
    /// 标记要序列化的属性或字段，即使它是私有的
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class JsonSerializeAttribute : JsonPropertyAttribute
    {
        public JsonSerializeAttribute() :
            base(true, null, true) { }
    }

    /// <summary>
    /// 标记要不序列化的字段属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class JsonDoNotSerializeAttribute : JsonPropertyAttribute
    {
        public JsonDoNotSerializeAttribute() :
            base(false, null, false) { }
    }
    
    /// <summary>
    /// 序列化前要调用的方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class JsonBeforeSerializationAttribute : Attribute { }

    /// <summary>
    /// 序列化后要调用的方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class JsonAfterDeserializationAttribute : Attribute { }

    /// <summary>
    /// 将属性标记为要使用其他名称序列化的字段
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class JsonSerializeAsAttribute : JsonPropertyAttribute
    {
        public JsonSerializeAsAttribute(string serializeName) :
            base(true, serializeName, true) { }
    }

    /// <summary>
    /// 标记属性或字段的序列化方式
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class JsonPropertyAttribute : Attribute
    {

        #region 变量 [VARIABLES]

        /// <summary>
        /// 是否可以被序列化
        /// </summary>
        public readonly bool Serializable;
        /// <summary>
        /// 序列化时的名称
        /// </summary>
        public readonly string SerializeName;
      
        /// <summary>
        /// 是否可以反序列化
        /// </summary>
        public readonly bool Deserializable;

        #endregion

        #region 构造函数 [CONSTRUCTOR]
        
        public JsonPropertyAttribute(bool serializable, string serializeName, bool deserializable)
        {
            Serializable = serializable;
            SerializeName = serializeName;
            Deserializable = deserializable;
        }
        
        #endregion
    }

    // ReSharper disable once InconsistentNaming
    public static partial class JsonUtility
    {
        /// <summary>
        /// 在序列化之前调用（反射元数据走 DefaultJson.ReflectionCache 缓存）
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="objectType">对象的类型</param>
        public static void PreSerialization(object obj, Type objectType)
        {
            if (obj == null || objectType == null) return;

            foreach (MethodInfo info in DefaultJson.ReflectionCache.Get(objectType).BeforeSerializeMethods)
            {
                info.Invoke(obj, null);
            }
        }
        
        /// <summary>
        /// 在反序列化之后调用（反射元数据走 DefaultJson.ReflectionCache 缓存）
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="objectType">对象的类型</param>
        public static void PostDeserialize(object obj, Type objectType)
        {
            if (obj == null || objectType == null) return;

            foreach (MethodInfo info in DefaultJson.ReflectionCache.Get(objectType).AfterDeserializeMethods)
            {
                info.Invoke(obj, null);
            }
        }
        
        /// <summary>
        /// 获取字段的序列化数据
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="field">目标字段</param>
        /// <param name="removeNulls">不序列化 null 的对象</param>
        /// <param name="key">序列化的键</param>
        /// <param name="value">序列化的值</param>
        /// <returns>是否需要序列化</returns>
        public static bool SerializeObject(object obj, FieldInfo field, bool removeNulls,
            out string key, out object value)
        {
            JsonPropertyAttribute jsonProperty = field.GetCustomAttribute<JsonPropertyAttribute>();
            
            // 判断字段是否不需要序列化
            bool forceExclude = field.Name[0] == '<' ||
                                TypeIsForbidden(field.FieldType) ||
                                jsonProperty?.Serializable == false;
            if (!forceExclude)
            {
                // 判断字段是否可以序列化
                if ( jsonProperty?.Serializable == true ||
                     (!field.IsInitOnly && !field.IsLiteral && !field.IsPrivate))
                {
                    value = field.GetValue(obj);
                    if (!removeNulls || value != null)
                    {
                        key = jsonProperty?.SerializeName ?? field.Name;
                        return true;
                    }
                }
            }

            key = null;
            value = null;
            return false;
        }

        /// <summary>
        /// 获取属性的序列化数据
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="property">目标属性</param>
        /// <param name="removeNulls">不序列化 null 的对象</param>
        /// <param name="key">序列化的键</param>
        /// <param name="value">序列化的值</param>
        /// <returns>是否需要序列化</returns>
        public static bool SerializeObject(object obj, PropertyInfo property, bool removeNulls,
            out string key, out object value)
        {
            JsonPropertyAttribute jsonProperty = property.GetCustomAttribute<JsonPropertyAttribute>();

            // 判断属性是否不需要序列化
            bool forceExclude = property.Name[0] == '<' ||
                                TypeIsForbidden(property.PropertyType) ||
                                jsonProperty?.Serializable == false ||
                                property.GetIndexParameters().Length > 0;
            if (!forceExclude)
            {
                // 判断属性是否可以序列化（默认要求读写兼备——往返对称契约，与 DefaultJson 对齐；
                // get-only 计算属性（如 Vector3.normalized）会产生无限装箱链，从构造上排除）
                if (jsonProperty?.Serializable == true ||
                    (property.CanRead && property.GetSetMethod() != null))
                {
                    value = property.GetValue(obj);
                    if (!removeNulls || value != null)
                    {
                        key = jsonProperty?.SerializeName ?? property.Name;
                        return true;
                    }
                }
            }
            
            key = null;
            value = null;
            return false;
        }

        /// <summary>
        /// 默认不序列化的类型（各 JsonHandler 通用契约）
        /// </summary>
        /// <param name="type">对象的类型</param>
        /// <returns></returns>
        /// <remarks>
        /// <see cref="UnityEngine.Object"/> 派生类型（GameObject/Component/Sprite/Texture/Material 等）一律排除：
        /// 反射式序列化会触达原生侧对象，既是性能陷阱也可能抛异常；Newtonsoft 侧同样无法（也不应）序列化它们。
        /// <see cref="UnityEngine.Events.UnityEvent"/> 非引擎对象派生，需单独列出。
        /// </remarks>
        public static bool TypeIsForbidden(Type type)
        {
            return type == typeof(UnityEvent) ||
                   typeof(UnityEngine.Object).IsAssignableFrom(type)
                ;
        }
        
        public static List<FieldInfo> GetAppropriateFields(Type type, object obj)
        {
            List<FieldInfo> result = new List<FieldInfo>();
            if (obj == null) return result;

            FieldInfo[] fi = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fi)
            {
                var forceExclude = field.Name[0] == '<';

                if (!forceExclude)
                {
                    var forceInclude = field.GetCustomAttribute<JsonSerializeAttribute>() != null || field.GetCustomAttribute<JsonSerializeAsAttribute>() != null;

                    if (forceInclude || (!field.IsInitOnly && !field.IsLiteral && !field.IsPrivate))
                    {
                        result.Add(field);
                    }
                }
            }

            if (type.BaseType != null && type.BaseType != typeof(object))
            {
                result.AddRange(GetAppropriateFields(type.BaseType, obj));
            }

            return result;
        }
    }
}