namespace Moirai.Atropos.Input
{
    /// <summary>
    /// UI 动作接口。
    /// </summary>
    public interface IUIAction
    { 
        /// <summary>
        /// 获取动作名称。
        /// </summary>
        string ActionName { get; }
    }
}