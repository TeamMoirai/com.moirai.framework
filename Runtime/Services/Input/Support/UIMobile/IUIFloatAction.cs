namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 浮点数值 UI 动作接口。
    /// </summary>
    public interface IUIFloatAction : IUIAction
    {
        /// <summary>
        /// 获取动作携带的浮点数值。
        /// </summary>
        float FloatValue { get; }
    }
}