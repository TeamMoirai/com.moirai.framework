using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程启动器，在 Awake 阶段启动 <see cref="ProcedureServiceSettings"/> 中配置的默认流程。
    /// </summary>
    public class ProcedureStarter : MonoBehaviour
    {
        private void Awake()
        {
            ProcedureServiceSettings.StartProcedure().Forget();
        }
    }
}