using System;
using System.Text;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON 格式存档处理器（未加密）。
    /// <para>编辑器下输出带缩进的可读 JSON 便于人工检查；真机走紧凑字节通路
    /// （框架内置 <see cref="JsonUtility"/> 的 <c>ToJsonBytes</c>/<c>ToObject&lt;T&gt;</c>，零 string 中间态）。</para>
    /// </summary>
    [Serializable]
    public class JsonSaveHandler : SaveServiceHandler
    {
        /// <summary>
        /// 将存档对象序列化为 UTF8 JSON 载荷字节。
        /// </summary>
        /// <param name="saveObject">存档对象。</param>
        /// <returns>JSON 载荷字节。</returns>
        protected internal override byte[] Serialize(object saveObject)
        {
#if UNITY_EDITOR
            // 编辑器保留可读格式便于人工检查存档；真机走紧凑字节通路
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(saveObject, true));
#else
            // 字节通路：直接产出 UTF8 JSON 字节，跳过 string 中间态与编码层
            return JsonUtility.ToJsonBytes(saveObject);
#endif
        }

        /// <summary>
        /// 从 JSON 载荷字节反序列化存档对象。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="payload">JSON 载荷字节。</param>
        /// <returns>反序列化后的对象。</returns>
        protected internal override T Deserialize<T>(byte[] payload)
        {
            // 直接解析字节（零 string 中间态；解析端已兼容 BOM 与编辑器可读格式）
            return JsonUtility.ToObject<T>(payload);
        }
    }
}
