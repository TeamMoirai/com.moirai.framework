namespace Moirai.GameMain.UI
{
    /// <summary>
    /// 引导窗绑定器的自检接缝。
    /// </summary>
    public interface ILauncherBinder
    {
        /// <summary>
        /// 汇总未接线的槽位名。
        /// </summary>
        /// <returns>全部接线时返回 <c>null</c>；否则返回以逗号分隔的槽位名。</returns>
        string DescribeUnboundSlots();
    }
}
