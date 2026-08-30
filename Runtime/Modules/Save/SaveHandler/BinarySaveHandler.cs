using System.IO;
using System;
using System.Runtime.Serialization.Formatters.Binary;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 二进制格式存档后端配置。
    /// </summary>
    [Serializable]
    public sealed class BinarySaveHandlerConfig : SaveServiceHandlerConfig
    {
        /// <inheritdoc />
        public override SaveServiceHandler CreateHandler()
        {
            return new BinarySaveHandler();
        }
    }

    /// <summary>
    /// 此保存加载方法将文件保存并加载为二进制文件
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: BinaryFormatter is vulnerable to deserialization attacks (RCE).
    /// It has been deprecated by Microsoft and is removed in .NET 9+.
    /// Consider migrating to JsonSaveHandler for new projects.
    /// See: https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide
    /// </remarks>
    [System.Obsolete("BinaryFormatter is insecure and deprecated. Use JsonSaveHandler instead. See https://aka.ms/binaryformatter")]
    public class BinarySaveHandler : SaveServiceHandler
    {
        private BinaryFormatter _formatter;

        private BinaryFormatter Formatter => _formatter ??= new BinaryFormatter();

        /// <summary>
        /// 序列化后将指定对象保存到指定位置的磁盘上
        /// </summary>
        protected internal override UniTask SerializeAsync(object objectToSave, FileStream saveFile)
        {
            Formatter.Serialize(saveFile, objectToSave);
            saveFile.Close();

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 从磁盘加载指定的文件并对其进行反序列化
        /// </summary>
        protected internal override UniTask<T> DeserializeAsync<T>(FileStream saveFile)
        {
            T savedObject = (T)Formatter.Deserialize(saveFile);
            saveFile.Close();

            return UniTask.FromResult(savedObject);
        }
    }
}
