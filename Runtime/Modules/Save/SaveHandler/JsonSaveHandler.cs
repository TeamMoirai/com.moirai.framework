using System.IO;
using System;
using System.Text;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// JSON 格式存档后端配置。
    /// </summary>
    [Serializable]
    public sealed class JsonSaveHandlerConfig : SaveServiceHandlerConfig
    {
        /// <inheritdoc />
        public override SaveServiceHandler CreateHandler()
        {
            return new JsonSaveHandler();
        }
    }

    /// <summary>
    /// JSON 格式存档处理器。
    /// <para>由 <see cref="JsonSaveHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化）。</para>
    /// </summary>
    public class JsonSaveHandler : SaveServiceHandler
    {
        /// <summary>
        /// 将指定的对象转换为 json 后将其保存在指定位置
        /// </summary>
        protected internal override UniTask SerializeAsync(object objectToSave, FileStream saveFile)
        {
#if UNITY_EDITOR
            // 编辑器保留可读格式便于人工检查存档；真机走紧凑字节通路
            string json = JsonUtility.ToJson(objectToSave, true);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
#else
            // 字节通路：直接产出 UTF8 JSON 字节写入文件，跳过 string 中间态与 StreamWriter 编码层
            byte[] bytes = JsonUtility.ToJsonBytes(objectToSave);
#endif
            saveFile.Write(bytes, 0, bytes.Length);
            saveFile.Close();

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 加载指定的文件并对其进行解码
        /// </summary>
        protected internal override UniTask<T> DeserializeAsync<T>(FileStream saveFile)
        {
            // 整体读为字节后直接解析（零 string 中间态；解析端已兼容 BOM 与编辑器可读格式）
            byte[] buffer = ReadAllBytes(saveFile);
            T savedObject = JsonUtility.ToObject<T>(buffer);
            saveFile.Close();

            return UniTask.FromResult(savedObject);
        }

        private static byte[] ReadAllBytes(FileStream stream)
        {
            long length = stream.Length;
            if (length > int.MaxValue)
            {
                throw new IOException("Save file is too large: " + length);
            }

            var buffer = new byte[(int)length];
            int read = 0;
            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk <= 0) break;
                read += chunk;
            }

            return read == buffer.Length ? buffer : TrimTrailingUnread(buffer, read);
        }

        private static byte[] TrimTrailingUnread(byte[] buffer, int read)
        {
            var exact = new byte[read];
            System.Array.Copy(buffer, exact, read);
            return exact;
        }
    }
}
