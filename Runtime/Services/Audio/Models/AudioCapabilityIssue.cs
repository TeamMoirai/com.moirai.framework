namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 一条「用到的能力 × 后端自报能力」冲突：<see cref="AssetPath"/> 指向出问题的资产，<see cref="Field"/> 是出问题的字段。
    /// </summary>
    /// <remarks>不带严重级别，全部按 Warning 上报；<see cref="Detail"/> 自带该改哪儿。</remarks>
    internal struct AudioCapabilityIssue
    {
        internal string AssetPath;
        internal string Field;
        internal string Detail;
    }
}
