namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 键值捕获格式（KVT）记录类型码——自有二进制键值格式的自描述载荷标识。
    /// <para>对象级记录带键（<c>[2B 键长][键 UTF8][1B 类型][载荷]</c>）；集合元素记录无键（<c>[1B 类型][载荷]</c>）；
    /// 嵌套对象载荷为 <c>[4B 字段数][对象级记录…]</c>。全部小端序。</para>
    /// </summary>
    public enum ESaveKvType : byte
    {
        /// <summary>空引用（字符串/嵌套对象/集合元素）。</summary>
        Null = 0,

        /// <summary>布尔（1B）。</summary>
        Bool = 1,

        /// <summary>有符号字节（1B）。</summary>
        SByte = 2,

        /// <summary>无符号字节（1B）。</summary>
        Byte = 3,

        /// <summary>有符号 16 位（2B）。</summary>
        Int16 = 4,

        /// <summary>无符号 16 位（2B）。</summary>
        UInt16 = 5,

        /// <summary>有符号 32 位（4B）。</summary>
        Int32 = 6,

        /// <summary>无符号 32 位（4B）。</summary>
        UInt32 = 7,

        /// <summary>有符号 64 位（8B）。</summary>
        Int64 = 8,

        /// <summary>无符号 64 位（8B）。</summary>
        UInt64 = 9,

        /// <summary>单精度浮点（4B）。</summary>
        Single = 10,

        /// <summary>双精度浮点（8B）。</summary>
        Double = 11,

        /// <summary>十进制（16B，4 个 int 分量）。</summary>
        Decimal = 12,

        /// <summary>字符（2B UTF-16 码元）。</summary>
        Char = 13,

        /// <summary>字符串（[4B 字节长][UTF8]，null 走 <see cref="Null"/>）。</summary>
        String = 14,

        /// <summary>二维向量（2 × float）。</summary>
        Vector2 = 20,

        /// <summary>三维向量（3 × float）。</summary>
        Vector3 = 21,

        /// <summary>四维向量（4 × float）。</summary>
        Vector4 = 22,

        /// <summary>四元数（4 × float，x/y/z/w）。</summary>
        Quaternion = 23,

        /// <summary>颜色（4 × float，r/g/b/a）。</summary>
        Color = 24,

        /// <summary>矩形（4 × float，x/y/width/height）。</summary>
        Rect = 25,

        /// <summary>包围盒（6 × float，center + extents）。</summary>
        Bounds = 26,

        /// <summary>日期时间（8B ticks + 1B DateTimeKind）。</summary>
        DateTime = 27,

        /// <summary>时间跨度（8B ticks）。</summary>
        TimeSpan = 28,

        /// <summary>序列（List/T[]/HashSet；[4B 元素数][元素记录…]）。</summary>
        Sequence = 30,

        /// <summary>映射（Dictionary；[4B 键值对数][键记录][值记录]…）。</summary>
        Map = 33,

        /// <summary>嵌套对象（<see cref="SaveDataAttribute"/> 标注类实例；[4B 字段数][对象级记录…]）。</summary>
        Object = 34,
    }
}
