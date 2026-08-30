using System.IO;

using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON 格式加密存档后端配置。
    /// </summary>
    [Serializable]
    public sealed class JsonEncryptedSaveHandlerConfig : SaveServiceHandlerConfig
    {
        /// <inheritdoc />
        public override bool IsEncrypted => true;

        /// <inheritdoc />
        public override SaveServiceHandler CreateHandler()
        {
            return new JsonEncryptedSaveHandler();
        }
    }

    /// <summary>
    /// 将指定位置的指定对象保存到磁盘上，转换为json并加密
    /// <para>由 <see cref="JsonEncryptedSaveHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化）。</para>
    /// </summary>
    public class JsonEncryptedSaveHandler : EncryptedSaveHandlerBase
    {
        protected override void SerializeToStream(object objectToSave, MemoryStream stream)
        {
            // 字节通路：直接产出 UTF8 JSON 字节，跳过 string 中间态与 StreamWriter 编码层
            byte[] json = JsonUtility.ToJsonBytes(objectToSave);
            stream.Write(json, 0, json.Length);
        }

        protected override T DeserializeFromStream<T>(MemoryStream stream)
        {
            // 已解密明文整体读为字节后直接解析（零 string 中间态）
            byte[] buffer = stream.ToArray();
            return JsonUtility.ToObject<T>(buffer);
        }
    }
}
