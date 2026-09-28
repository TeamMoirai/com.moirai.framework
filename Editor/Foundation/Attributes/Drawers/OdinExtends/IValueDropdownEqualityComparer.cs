using System;
using System.Collections.Generic;

namespace Sirenix.OdinInspector.Editor.Drawers
{
    /// <summary>
    /// <see cref="ValueDropdownItem"/> 下拉框值比较器：比较前先解包 <see cref="ValueDropdownItem"/> 取实际值，并可选按 <see cref="Type"/> 而非对象实例进行查找。
    /// </summary>
    internal class IValueDropdownEqualityComparer : IEqualityComparer<object>
    {
        /// <summary>
        /// 是否按 <see cref="Type"/> 进行查找比较。
        /// </summary>
        private readonly bool _isTypeLookup;

        /// <summary>
        /// 创建下拉框值比较器。
        /// </summary>
        /// <param name="isTypeLookup">是否按 <see cref="Type"/> 进行查找比较。</param>
        public IValueDropdownEqualityComparer(bool isTypeLookup) => _isTypeLookup = isTypeLookup;

        /// <summary>
        /// 判断两个对象是否相等：先解包 <see cref="ValueDropdownItem"/> 取实际值比较；启用类型查找时，进一步退化为比较两者的 <see cref="Type"/>。
        /// </summary>
        /// <param name="x">待比较的第一个对象，可为 <see cref="ValueDropdownItem"/>。</param>
        /// <param name="y">待比较的第二个对象，可为 <see cref="ValueDropdownItem"/>。</param>
        /// <returns>相等返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public new bool Equals(object x, object y)
        {
            if (x is ValueDropdownItem)
                x = ((ValueDropdownItem) x).Value;
            if (y is ValueDropdownItem)
                y = ((ValueDropdownItem) y).Value;
            if (EqualityComparer<object>.Default.Equals(x, y))
                return true;
            if (x == null != (y == null) || !_isTypeLookup)
                return false;
            Type type1 = x as Type;
            if ((object) type1 == null)
                type1 = x.GetType();
            Type type2 = type1;
            Type type3 = y as Type;
            if ((object) type3 == null)
                type3 = y.GetType();
            Type type4 = type3;
            return type2 == type4;
        }

        /// <summary>
        /// 获取对象的哈希码：先解包 <see cref="ValueDropdownItem"/> 取实际值；启用类型查找时返回其 <see cref="Type"/> 的哈希码。
        /// </summary>
        /// <param name="obj">待计算哈希码的对象，可为 <see cref="ValueDropdownItem"/>。</param>
        /// <returns>哈希码，入参解包后为 <c>null</c> 时返回 -1。</returns>
        public int GetHashCode(object obj)
        {
            if (obj == null)
                return -1;
            if (obj is ValueDropdownItem)
                obj = ((ValueDropdownItem) obj).Value;
            if (obj == null)
                return -1;
            if (!_isTypeLookup)
                return obj.GetHashCode();
            Type type = obj as Type;
            if ((object) type == null)
                type = obj.GetType();
            return type.GetHashCode();
        }
    }
}