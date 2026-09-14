using System;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入动作查询键（分组 + 名称的值类型组合键）。
    /// <para>替代每帧 "Group/Name" 字符串拼接：字典散列/相等比较直接作用于两个字符串引用，
    /// 查询热路径零分配；仅缓存未命中解析时才合成全限定名字符串。</para>
    /// </summary>
    internal readonly struct InputActionKey : IEquatable<InputActionKey>
    {
        private readonly string _group;
        private readonly string _name;

        public string Group => _group;
        public string Name => _name;

        public InputActionKey(string group, string name)
        {
            _group = group ?? string.Empty;
            _name = name ?? string.Empty;
        }

        /// <summary>
        /// 是否为全限定查询（分组非空）。
        /// </summary>
        public bool HasGroup => _group.Length > 0;

        /// <summary>
        /// 合成 "Group/Name" 全限定名（仅缓存未命中路径调用，禁止用于热路径）。
        /// </summary>
        public string ToFullName()
        {
            return HasGroup ? string.Concat(_group, "/", _name) : _name;
        }

        public bool Equals(InputActionKey other)
        {
            return string.Equals(_group, other._group, StringComparison.Ordinal) &&
                   string.Equals(_name, other._name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InputActionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(_group) * 397) ^ StringComparer.Ordinal.GetHashCode(_name);
            }
        }
    }
}
