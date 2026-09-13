using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON 序列化后端（框架内置 <see cref="JsonUtility"/> 零分配字节通路，默认后端）。
    /// <para>无类型标注要求，任意可序列化 POCO 开箱即用；字节始终为紧凑 UTF8 JSON（容器本身为二进制，块内不再缩进美化）。</para>
    /// </summary>
    public sealed class JsonSaveSerializer : ISaveSerializer
    {
        /// <summary>
        /// 后端标识（恒为 <see cref="ESaveBackend.Json"/>）。
        /// </summary>
        public ESaveBackend Backend => ESaveBackend.Json;

        /// <summary>
        /// 将数据对象序列化为紧凑 UTF8 JSON 字节。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="data">数据对象。</param>
        /// <returns>JSON 字节。</returns>
        public byte[] Serialize<T>(T data)
        {
            // 字节通路：直接产出 UTF8 JSON 字节，跳过 string 中间态与编码层
            return JsonUtility.ToJsonBytes(data);
        }

        /// <summary>
        /// 从 JSON 字节反序列化数据对象（解析端已兼容 BOM）。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="bytes">JSON 字节。</param>
        /// <returns>反序列化后的数据对象。</returns>
        public T Deserialize<T>(byte[] bytes)
        {
            return JsonUtility.ToObject<T>(bytes);
        }
    }
}
