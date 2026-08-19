using System.IO;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    public interface ISaveHandler
    {
        UniTask Save(object objectToSave, FileStream saveFile);
        UniTask<T> Load<T>(FileStream saveFile);
    }
}