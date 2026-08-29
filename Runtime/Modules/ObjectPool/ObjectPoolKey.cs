using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用池查找键（类型 + 池名），值语义判等防装箱。
    /// </summary>
    internal readonly struct ObjectPoolKey : IEquatable<ObjectPoolKey>
    {
        #region 字段 [FIELDS]

        private readonly Type _type;
        private readonly string _name;
        private readonly int _hashCode;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="ObjectPoolKey"/> 的新实例。
        /// </summary>
        /// <param name="type">对象类型。</param>
        /// <param name="name">池名称。</param>
        public ObjectPoolKey(Type type, string name)
        {
            _type = type ?? throw new ArgumentNullException(nameof(type));
            _name = name ?? string.Empty;
            unchecked
            {
                _hashCode = (_type.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(_name);
            }
        }

        #endregion

        #region 判等 [EQUALITY]

        /// <summary>
        /// 判断与另一键是否相等。
        /// </summary>
        /// <param name="other">另一键。</param>
        /// <returns>是否相等。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ObjectPoolKey other)
        {
            return _type == other._type && string.Equals(_name, other._name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断与指定对象是否相等。
        /// </summary>
        /// <param name="obj">指定对象。</param>
        /// <returns>是否相等。</returns>
        public override bool Equals(object obj)
        {
            return obj is ObjectPoolKey other && Equals(other);
        }

        /// <summary>
        /// 获取预计算哈希值。
        /// </summary>
        /// <returns>哈希值。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _hashCode;
        }

        /// <summary>
        /// 输出键的字符串表示（类型全名[.池名]）。
        /// </summary>
        /// <returns>字符串表示。</returns>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(_name))
            {
                return _type.FullName ?? _type.Name;
            }

            return StringUtility.Concat(_type.FullName ?? _type.Name, ".", _name);
        }

        #endregion
    }
}
