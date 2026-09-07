namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 布尔值 UI 动作接口。
    /// </summary>
    public interface IUIBoolAction : IUIAction
    {
        /// <summary>
        /// 获取动作携带的布尔值。
        /// </summary>
        bool BoolValue { get; }
    }
}