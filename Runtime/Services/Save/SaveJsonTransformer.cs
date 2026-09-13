#if NEWTONSOFT_JSON_INSTALLED
using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON 块载荷迁移变换器（Newtonsoft JObject DOM）：顶层属性改名/改型。
    /// <para>仅作用于块根对象的顶层属性（存档 POCO 字段层）；嵌套对象内部字段的迁移用
    /// <see cref="SaveMigrationContext.TransformBlock{T}"/> 以旧类型整对象读出后改写。</para>
    /// <para>改型建议限定基元/字符串/DateTime（DOM 原生类型）；复杂类型改型同样走 <see cref="SaveMigrationContext.TransformBlock{T}"/>。
    /// 依赖 Newtonsoft.Json（<c>com.unity.nuget.newtonsoft-json</c>）——未安装时字段级操作记录迁移失败（fail-fast）。</para>
    /// </summary>
    internal static class SaveJsonTransformer
    {
#if NEWTONSOFT_JSON_INSTALLED
        /// <summary>UTF-8 编解码器（无 BOM）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 将块根对象的顶层属性 <paramref name="oldField"/> 改名为 <paramref name="newField"/>。
        /// </summary>
        /// <param name="source">源块载荷（紧凑 UTF8 JSON）。</param>
        /// <param name="oldField">旧字段名。</param>
        /// <param name="newField">新字段名。</param>
        /// <param name="result">变换后的块载荷（未命中时为源引用）。</param>
        /// <returns>存在命中属性返回 <c>true</c>；根非 JSON 对象抛 <see cref="SaveKvFormatException"/>（归一为迁移失败）。</returns>
        public static bool RenameField(byte[] source, string oldField, string newField, out byte[] result)
        {
            JObject root = ParseRoot(source);
            JToken value = root[oldField];
            if (value == null)
            {
                result = source;
                return false;
            }

            root.Remove(oldField);
            root[newField] = value;
            result = s_Utf8.GetBytes(root.ToString(Formatting.None));
            return true;
        }

        /// <summary>
        /// 将块根对象的顶层属性 <paramref name="field"/> 改型（DOM 读出旧值 → 转换 → 写回新值）。
        /// </summary>
        /// <typeparam name="TOld">旧值类型（DOM 反序列化目标，建议基元/字符串/DateTime）。</typeparam>
        /// <typeparam name="TNew">新值类型。</typeparam>
        /// <param name="source">源块载荷（紧凑 UTF8 JSON）。</param>
        /// <param name="field">目标字段名。</param>
        /// <param name="convert">值转换器。</param>
        /// <param name="result">变换后的块载荷（未命中时为源引用）。</param>
        /// <returns>存在命中属性返回 <c>true</c>。</returns>
        public static bool RetypeField<TOld, TNew>(byte[] source, string field, Func<TOld, TNew> convert, out byte[] result)
        {
            JObject root = ParseRoot(source);
            JToken token = root[field];
            if (token == null)
            {
                result = source;
                return false;
            }

            TOld oldValue = token.Type == JTokenType.Null ? default : token.ToObject<TOld>();
            root[field] = JToken.FromObject(convert(oldValue));
            result = s_Utf8.GetBytes(root.ToString(Formatting.None));
            return true;
        }

        /// <summary>
        /// 解析块载荷为 JSON 根对象（根非对象归一为格式异常）。
        /// </summary>
        /// <param name="source">块载荷字节。</param>
        /// <returns>根对象。</returns>
        private static JObject ParseRoot(byte[] source)
        {
            JToken root = JToken.Parse(s_Utf8.GetString(source));
            if (root is not JObject rootObject)
            {
                throw new SaveKvFormatException("JSON save block root is not an object.");
            }

            return rootObject;
        }
#endif
    }
}
