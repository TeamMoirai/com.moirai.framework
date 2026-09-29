using Moirai.Atropos.Procedure;

namespace Moirai.Main
{
    /// <summary>
    /// 进入游戏流程前的流程基类。
    /// </summary>
    [ProcedureLauncher]
    public abstract class ProcedurePremainBase : ProcedureBase
    {
        /// <summary>
        /// 获取流程是否使用原生对话框；特殊流程（如游戏逻辑对话框资源更新完成前）可据此用原生对话框提示。
        /// </summary>
        public abstract bool UseNativeDialog { get; }
    }
}