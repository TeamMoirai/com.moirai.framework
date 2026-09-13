using System;
using System.Reflection;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据类型描述符（静态泛型缓存）：每类型仅在首次触达时反射读取 <see cref="SaveDataAttribute"/> 一次（AOT 安全，无每调用反射开销）。
    /// </summary>
    /// <typeparam name="T">存档数据类型。</typeparam>
    internal static class SaveBlockDescriptor<T>
    {
        /// <summary>
        /// 是否声明了 <see cref="SaveDataAttribute"/>。
        /// </summary>
        internal static readonly bool HasAttribute;

        /// <summary>
        /// 声明的块键（未声明时为 null）。
        /// </summary>
        internal static readonly string Key;

        /// <summary>
        /// 声明的当前数据模式版本（未声明时为 1）。
        /// </summary>
        internal static readonly int Version;

        /// <summary>
        /// 声明的序列化后端（未声明时为 <see cref="ESaveBackend.Json"/>）。
        /// </summary>
        internal static readonly ESaveBackend Backend;

        /// <summary>
        /// 类型是否为 <see cref="SaveDataBlock"/> 子类（决定加载期版本迁移与版本保护是否生效）。
        /// </summary>
        internal static readonly bool IsMigrationAware;

        static SaveBlockDescriptor()
        {
            SaveDataAttribute attribute = typeof(T).GetCustomAttribute<SaveDataAttribute>(false);
            if (attribute != null)
            {
                HasAttribute = true;
                Key = attribute.Key;
                Version = Math.Max(1, attribute.Version);
                Backend = attribute.Backend;
            }
            else
            {
                Version = 1;
                Backend = ESaveBackend.Json;
            }

            IsMigrationAware = typeof(SaveDataBlock).IsAssignableFrom(typeof(T));
        }
    }
}
