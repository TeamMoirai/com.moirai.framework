namespace Testing
{
    /// <summary>
    /// AudioGroupConfigs 能力探测类环境 Ignore 的共享口径：PlayMode 侧 17 处共用的 Ignore 消息文本，单点维护。
    /// </summary>
    /// <remarks>
    /// 环境性 <c>Assert.Ignore</c> 须写明「探测能力 + 恢复条件」（Testing.md）；重复的理由文本抽成共享 <c>const</c>。
    /// 同程序集进 Support，跨程序集不可共享（asmdef 拓扑），单文件场景用私有 <c>const</c>（如 Player 侧 <c>AudioPerformanceTests</c>）。
    /// 改口径只动这里，17 个用例的 Ignore 消息随之同步。
    /// </remarks>
    internal static class AudioGroupIgnoreReasons
    {
        public const string Prefix = "AudioGroupConfigs 未配置";

        public const string Probe = "探测能力：宿主工程 AudioServiceSettings 是否配置了可用的音频组";

        public const string Recover = "恢复条件：宿主配置 AudioGroupConfigs 后本格完整执行";

        public const string RecoverOrTestHost =
            "恢复条件：宿主配置 AudioGroupConfigs（或改走 AudioServiceTestHost 自建最小组）后本格完整执行";

        /// <summary>常规环境跳过：宿主补配音频组后本格即可完整执行。</summary>
        public static string Skip(string what)
        {
            return Prefix + "，" + what + "。" + Probe + "；" + Recover + "。";
        }

        /// <summary>带 TestHost 替代路径的环境跳过：关键路径也可改走代码内建最小组执行。</summary>
        public static string SkipOrTestHost(string what)
        {
            return Prefix + "，" + what + "。" + Probe + "；" + RecoverOrTestHost + "。";
        }
    }
}
