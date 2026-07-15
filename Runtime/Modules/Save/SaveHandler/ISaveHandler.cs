using System.IO;
using System.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    public interface ISaveHandler
    {
        Task Save(object objectToSave, FileStream saveFile);
        Task<T> Load<T>(FileStream saveFile);
    }
}