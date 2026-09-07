using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assertions;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    public static partial class ReflectionUtility
    {
        /// <summary>
        /// 获取指定类型及其继承层次中的所有字段。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="flags">检索字段时使用的绑定标志。</param>
        /// <returns>该类型的所有字段。</returns>
        public static List<FieldInfo> GetAllFields(this Type type, BindingFlags flags)
        {
            // 若为 object 基类型则提前返回
            if (type == typeof(object))
            {
                return new List<FieldInfo>();
            }

            // 递归调用
            var fields = type.BaseType.GetAllFields(flags);
            fields.AddRange(type.GetFields(flags | BindingFlags.DeclaredOnly));
            return fields;
        }

        /// <summary>
        /// 对对象执行深拷贝。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="obj">待拷贝的对象。</param>
        /// <returns>obj 的深拷贝副本。</returns>
        /// <exception cref="ArgumentNullException">对象不能为 null。</exception>
        public static T DeepCopy<T>(T obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "object can not be null");
            }
            return (T)DoCopy(obj);
        }


        /// <summary>
        /// 递归执行拷贝。
        /// </summary>
        /// <param name="obj">待拷贝的对象。</param>
        /// <returns>拷贝出的新对象。</returns>
        /// <exception cref="ArgumentException">遇到不支持拷贝的类型。</exception>
        private static object DoCopy(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            // 值类型
            var type = obj.GetType();
            if (type.IsValueType || type == typeof(string))
            {
                return obj;
            }

            // 数组

            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                var array = obj as Array;
                Array copied = Array.CreateInstance(elementType, array.Length);
                for (int i = 0; i < array.Length; i++)
                {
                    copied.SetValue(DoCopy(array.GetValue(i)), i);
                }
                return Convert.ChangeType(copied, obj.GetType());
            }

            // Unity Object 引用类型
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return obj;
            }

            // 类类型 -> 递归拷贝
            if (type.IsClass)
            {
                var copy = Activator.CreateInstance(obj.GetType());

                var fields = type.GetAllFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (FieldInfo field in fields)
                {
                    var fieldValue = field.GetValue(obj);
                    if (fieldValue != null)
                    {
                        field.SetValue(copy, DoCopy(fieldValue));
                    }
                }

                return copy;
            }

            // 兜底处理
            throw new ArgumentException("Unknown type");
        }
        
                
        /// <summary>
        /// 获取类型（沿继承链向上查找）的第一个泛型实参类型。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>第一个泛型实参的类型。</returns>
        public static Type GetGenericArgumentType(Type type)
        {
            /* 防止无限递归 */
            const int maxDepth = 10;
            int depth = 0;
            while (type != null && !type.IsGenericType && depth < maxDepth)
            {
                type = type.BaseType;
                depth ++;
            }
            Assert.IsNotNull(type);
            Assert.IsTrue(type.GetGenericArguments().Length > 0);
            return type.GetGenericArguments()[0];
        }
                
        /// <summary>
        /// 判断类型自身、其基类或实现的接口是否继承自指定泛型定义。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <param name="genericDefinition">泛型定义类型（如 <c>typeof(List&lt;&gt;)</c>）。</param>
        /// <returns>继承自该泛型定义返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool IsInheritedFromGenericDefinition(Type type, Type genericDefinition)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition)
            {
                return true;
            }
            
            if (type.BaseType != null)
            {
                if (IsInheritedFromGenericDefinition(type.BaseType, genericDefinition))
                {
                    return true;
                }
            }
            
            foreach (var interfaceType in type.GetInterfaces())
            {
                if (IsInheritedFromGenericDefinition(interfaceType, genericDefinition))
                {
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// 获取类型中 Unity 可序列化的所有字段名称。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>可序列化字段的名称列表。</returns>
        public static List<string> GetSerializedFieldsName(Type type)
        {
            List<FieldInfo> fieldInfos = new List<FieldInfo>();
            GetAllSerializableFields(type, fieldInfos);
            return fieldInfos.Select(x => x.Name)
                            .ToList();
        }
         
        /// <summary>
        /// 获取类型中 Unity 可序列化的所有字段。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>可序列化字段的 <see cref="FieldInfo"/> 列表。</returns>
        public static List<FieldInfo> GetSerializedFields(Type type)
        {
            List<FieldInfo> fieldInfos = new List<FieldInfo>();
            GetAllSerializableFields(type, fieldInfos);
            return fieldInfos;
        }
         
        private static void GetAllSerializableFields(Type type, List<FieldInfo> fieldInfos)
        {
            if (type.BaseType != null)
            {
                GetAllSerializableFields(type.BaseType, fieldInfos);
            }

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public 
                                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var field in fields)
            {
                if (!ValidateSerializedField(field))
                {
                    continue;
                }

                fieldInfos.Add(field);
            }
        }

        private static bool ValidateSerializedField(FieldInfo field)
        {
            if (field.IsStatic) return false;

            if (field.IsInitOnly) return false;

            if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField), false)) return false;

            if (!field.FieldType.IsSerializable && !typeof(UObject).IsAssignableFrom(field.FieldType)
                && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum
                && !typeof(List<>).IsAssignableFrom(field.FieldType) && !field.FieldType.IsArray
                && !field.FieldType.IsGenericType
                && !Attribute.IsDefined(field.FieldType, typeof(SerializableAttribute), false)
                && !IsUnityBuiltinTypes(field.FieldType))
                return false;

            return true;
        }

        private static readonly HashSet<Type> s_SerializableNumericTypes = new HashSet<Type>()
        {
            typeof(byte), typeof(sbyte),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(short), typeof(ushort),
            typeof(float),
            typeof(double),
            typeof(bool)
        };

        /// <summary>
        /// 判断类型是否为可序列化的数值类型（整数、浮点或布尔）。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>是数值类型返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool IsSerializableNumericTypes(Type type)
        {
            return s_SerializableNumericTypes.Contains(type);
        }
        
        private static readonly HashSet<Type> s_UnityBuiltinTypes = new HashSet<Type>()
        {
            typeof(AnimationCurve), 
            typeof(Bounds), typeof(BoundsInt), 
            typeof(Color),
            typeof(UObject),
            typeof(Quaternion), 
            typeof(Rect), typeof(RectInt), 
            typeof(Vector2), typeof(Vector2Int),
            typeof(Vector3), typeof(Vector3Int), 
            typeof(Vector4)
        };
        

        /// <summary>
        /// 判断类型是否为 Unity 内置类型（如 Color、Vector3、Bounds 等）。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>是内置类型返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool IsUnityBuiltinTypes(Type type)
        {
            return s_UnityBuiltinTypes.Contains(type);
        }
        
        /// <summary>
        /// 创建指定类型的默认值：数组返回空数组，值类型返回零值，字符串返回空串，其余尝试调用无参构造，失败返回 null。
        /// </summary>
        /// <param name="type">目标类型。</param>
        /// <returns>默认值实例，无法创建时为 null。</returns>
        public static object CreateDefaultValue(Type type)
        {
            if (type.IsArray)
            {
                return Array.CreateInstance(type.GetElementType()!, 0);
            }
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }

            if (type == typeof(string))
            {
                return string.Empty;
            }
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
    }
}