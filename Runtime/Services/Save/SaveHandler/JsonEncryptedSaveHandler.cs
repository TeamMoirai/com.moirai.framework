using System;
using System.IO;
using Moirai.Atropos;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON + AES 加密存档处理器：明文侧走紧凑 JSON 字节通路，密文侧由 <see cref="EncryptedSaveHandlerBase"/> 统一加密。
    /// </summary>
    [Serializable]
    public class JsonEncryptedSaveHandler : EncryptedSaveHandlerBase
    {
        /// <summary>
        /// 将存档对象序列化为紧凑 JSON 字节并写入明文流。
        /// </summary>
        /// <param name="objectToSave">存档对象。</param>
        /// <param name="stream">明文输出流。</param>
        protected override void SerializeToStream(object objectToSave, MemoryStream stream)
        {
            // 字节通路：直接产出 UTF8 JSON 字节，跳过 string 中间态与编码层
            byte[] json = JsonUtility.ToJsonBytes(objectToSave);
            stream.Write(json, 0, json.Length);
        }

        /// <summary>
        /// 从明文流反序列化存档对象。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="stream">明文输入流。</param>
        /// <returns>反序列化后的对象。</returns>
        protected override T DeserializeFromStream<T>(MemoryStream stream)
        {
            // 已解密明文整体读为字节后直接解析（零 string 中间态）
            byte[] buffer = stream.ToArray();
            return JsonUtility.ToObject<T>(buffer);
        }
    }
}
