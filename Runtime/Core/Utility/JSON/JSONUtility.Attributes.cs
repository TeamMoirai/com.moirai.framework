using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Moirai.Atropos
{
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
    public static partial class JSONUtility
    {
        /// <summary>
        /// 在序列化之前调用
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="objectType">对象的类型</param>
        public static void PreSerialization(object obj, Type objectType)
        {
            foreach (MethodInfo info in objectType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (info.GetCustomAttribute<JsonBeforeSerializationAttribute>() != null)
                {
                    info.Invoke(obj, null);
                }
            }
        }
        
        /// <summary>
        /// 在反序列化之后调用
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <param name="objectType">对象的类型</param>
        public static void PostDeserialize(object obj, Type objectType)
        {
            foreach (MethodInfo info in objectType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (info.GetCustomAttribute<JsonAfterDeserializationAttribute>() != null)
                {
                    info.Invoke(obj, null);
                }
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
                // 判断属性是否可以序列化
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
        /// 默认不序列化的类型
        /// </summary>
        /// <param name="type">对象的类型</param>
        /// <returns></returns>
        public static bool TypeIsForbidden(Type type)
        {
            return type == typeof(UnityEvent) ||
                   type == typeof(Sprite) ||
                   type == typeof(Texture)
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