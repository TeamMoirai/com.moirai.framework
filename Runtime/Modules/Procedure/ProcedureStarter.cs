using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    public class ProcedureStarter : MonoBehaviour
    {
        private void Awake()
        {
            ProcedureServiceSettings.StartProcedure().Forget();
        }
    }
}