using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 内置流程启动器，在 Awake 阶段启动 <see cref="ProcedureServiceSettings"/> 中配置的默认流程。
    /// </summary>
    public class BuildInProcedureStarter : MonoBehaviour
    {
        private void Awake()
        {
            StartProcedure().Forget();
        }
        
        /// <summary>
        /// 启动流程（引导入口，失败 fail-fast）。
        /// <para>流程服务未注册、类型解析失败或入口流程无效时抛出 <see cref="GameException"/>——
        /// 启动链配置错误属发布级缺陷，静默吞掉会让玩家面对永久黑屏；异常经 <c>Forget()</c> 转为
        /// <c>UniTaskScheduler.UnobservedTaskException</c> 输出错误日志，调用方不应捕获吞掉。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌（由 <see cref="BuildInProcedureStarter"/> 传入宿主销毁令牌，
        /// 避免退出播放/应用后让帧续体撞上域拆除）。</param>
        private static async UniTask StartProcedure(CancellationToken cancellationToken = default)
        {
            if (!ProcedureService.IsValid)
            {
                throw new GameException(
                    "ProcedureService is not registered when StartProcedure is called — " +
                    "ensure GameApp has initialized the service world before ProcedureStarter awakes.");
            }

            var availableProcedureTypeNames = ProcedureServiceSettings.AvailableProcedureTypeNames;
            var entranceProcedureTypeName = ProcedureServiceSettings.EntranceProcedureTypeName;
            
            ProcedureBase[] procedures = new ProcedureBase[availableProcedureTypeNames.Length];
            ProcedureBase entranceProcedure = null;
            for (int i = 0; i < availableProcedureTypeNames.Length; i++)
            {
                Type procedureType = AssemblyUtility.GetType(availableProcedureTypeNames[i]);
                if (procedureType == null)
                {
                    throw new GameException(StringUtility.Format(
                        "Can not find procedure type '{0}'.", availableProcedureTypeNames[i]));
                }

                procedures[i] = (ProcedureBase)Activator.CreateInstance(procedureType);
                if (procedures[i] == null)
                {
                    throw new GameException(StringUtility.Format(
                        "Can not create procedure instance '{0}'.", availableProcedureTypeNames[i]));
                }

                if (entranceProcedureTypeName == availableProcedureTypeNames[i])
                {
                    entranceProcedure = procedures[i];
                }
            }

            if (entranceProcedure == null)
            {
                throw new GameException("Entrance procedure is invalid.");
            }

            ProcedureService.Initialize(procedures);

            // 让出一帧：流程 OnInit 内按框架约定经 MainThreadDispatcher 排队的场景对象访问在下一帧执行，
            // 须等其落地后再进入首个流程的 OnEnter
            await UniTask.Yield(cancellationToken);

            ProcedureService.StartProcedure(entranceProcedure.GetType());
        }        
    }
}