using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    public class JsonSaveHandler : ISaveHandler
    {
        /// <summary>
        /// 将指定的对象转换为 json 后将其保存在指定位置
        /// </summary>
        /// <param name="objectToSave"></param>
        /// <param name="saveFile"></param>
        public Task Save(object objectToSave, FileStream saveFile)
        {
            string json = JSONUtility.ToJson(objectToSave
#if UNITY_EDITOR
            , true
#endif
            );
            StreamWriter streamWriter = new StreamWriter(saveFile);
            streamWriter.Write(json);
            streamWriter.Close();
            saveFile.Close();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 加载指定的文件并对其进行解码
        /// </summary>
        /// <param name="saveFile"></param>
        /// <returns></returns>
        public Task<T> Load<T>(FileStream saveFile)
        {
            StreamReader streamReader = new StreamReader(saveFile, Encoding.UTF8);
            string json = streamReader.ReadToEnd();
            T savedObject = JSONUtility.ToObject<T>(json);
            streamReader.Close();
            saveFile.Close();

            return Task.FromResult(savedObject);
        }
    }
}