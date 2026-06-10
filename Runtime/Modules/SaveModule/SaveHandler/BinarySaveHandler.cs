using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 此保存加载方法将文件保存并加载为二进制文件
    /// </summary>
    public class BinarySaveHandler : ISaveHandler
    {
        private readonly BinaryFormatter _formatter = new BinaryFormatter();

        /// <summary>
        /// 序列化后将指定对象保存到指定位置的磁盘上
        /// </summary>
        /// <param name="objectToSave"></param>
        /// <param name="saveFile"></param>
        public Task Save(object objectToSave, FileStream saveFile)
        {
            _formatter.Serialize(saveFile, objectToSave);
            saveFile.Close();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 从磁盘加载指定的文件并对其进行反序列化
        /// </summary>
        /// <param name="saveFile"></param>
        /// <returns></returns>
        public Task<T> Load<T>(FileStream saveFile)
        {
            T savedObject = (T)_formatter.Deserialize(saveFile);
            saveFile.Close();

            return Task.FromResult(savedObject);
        }
    }
}