namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 一条配置自检结果：<see cref="Field"/> 是设置项名，<see cref="Detail"/> 说清"为什么会出问题"与建议取值。
    /// <para>刻意不带严重级别——自检只报不改，全部落 Warning。</para>
    /// </summary>
    internal struct ResourceSettingsIssue
    {
        internal string Field;
        internal string Detail;
    }

    /// <summary>
    /// 设置项自检：把"值单看合法、但相互关系不成立"的配置挑出来。
    /// <para><b>只报不改</b>：夹取会掩盖配置错误，而这些值本身都合法，只是永远不参与决策——它在 Inspector
    /// 里看得见、在版本库里能 diff，沉默的代价是带着一个从未被读取的值发到发行版，排查时人人都以为调过它了。
    /// 这条政策与 <see cref="PlayMode"/> 读取时"只归一返回值、不回写资产"是同一个取向。</para>
    /// <para>判据以数组形式交给调用方（调用方持数组，与本包诊断面同形），所以同一份规则既能被
    /// <c>ResourceService.OnInit</c> 在启动时打一次，也能被构建期检查原样复用，不必两处各写一遍。</para>
    /// </summary>
    public sealed partial class ResourceServiceSettings
    {
        /// <summary>
        /// 收集配置问题，返回实际写入条数。缓冲区不足时少报而不抛——自检永远不该成为故障源。
        /// </summary>
        internal int GetConfigurationIssues(ResourceSettingsIssue[] results, int maxCount)
        {
            if (results == null || maxCount <= 0)
            {
                return 0;
            }

            int written = 0;

            // 帧驱动取二者较大值：卸载档配得比每帧还小，就永远被每帧值顶掉。
            if (m_ExpireProcessCountWhenUnloading < m_ExpireProcessCountPerFrame)
            {
                written += Add(results, written, maxCount, nameof(m_ExpireProcessCountWhenUnloading),
                    "ExpireProcessCountWhenUnloading ({0}) is below ExpireProcessCountPerFrame ({1}): the unload-time " +
                    "budget never takes effect, because the frame drive keeps the larger of the two.",
                    m_ExpireProcessCountWhenUnloading, m_ExpireProcessCountPerFrame);
            }

            // 销毁态回收完全依赖这条轮转配额：非正数意味着它一帧都不跑。
            if (m_DestroySweepBudget <= 0)
            {
                written += Add(results, written, maxCount, nameof(m_DestroySweepBudget),
                    "DestroySweepBudget is {0}: the destroyed owner/binding sweep never runs, so leases held by objects " +
                    "whose OnDestroy was truncated are never reclaimed. Keep it at or above 1.", m_DestroySweepBudget);
            }

            // 过期刻度按秒落进 256 格时间轮：超过一整圈的存活期不会提前释放，但会被跳过、
            // 直到轮盘绕回来才重新遇到——表现为释放延迟最多整整一圈，而没有任何异常。
            if (m_IdleAssetExpireTime > ResourceRecordStore.IdleWheelSpanSeconds)
            {
                written += Add(results, written, maxCount, nameof(m_IdleAssetExpireTime),
                    "IdleAssetExpireTime ({0}s) is longer than the expiry wheel spans ({1} ticks): entries are skipped " +
                    "until the wheel wraps around once, so release is late by up to a full lap. Keep it at or below {1}.",
                    m_IdleAssetExpireTime, ResourceRecordStore.IdleWheelSpanSeconds);
            }

            // 卸载调度是"距上次卸载 >= 上限即触发"：非正数让这个比较恒真，卸载变成每帧一次。
            if (m_MaxUnloadUnusedAssetsInterval <= 0f)
            {
                written += Add(results, written, maxCount, nameof(m_MaxUnloadUnusedAssetsInterval),
                    "MaxUnloadUnusedAssetsInterval is {0}: the periodic unload trigger compares elapsed time against this " +
                    "value, so a non-positive one unloads unused assets every frame. Keep it above 0.",
                    m_MaxUnloadUnusedAssetsInterval);
            }

            // 下限只门控"预约请求"那条路：它比上限还大，预约档就永远先被上限触发，等于白配。
            if (m_MinUnloadUnusedAssetsInterval > m_MaxUnloadUnusedAssetsInterval)
            {
                written += Add(results, written, maxCount, nameof(m_MinUnloadUnusedAssetsInterval),
                    "MinUnloadUnusedAssetsInterval ({0}) is above MaxUnloadUnusedAssetsInterval ({1}): preordered unloads " +
                    "are already released by the periodic trigger, so the min interval never decides anything.",
                    m_MinUnloadUnusedAssetsInterval, m_MaxUnloadUnusedAssetsInterval);
            }

            // GC 节流同理：负值等于每次请求都真收。
            if (m_MinGCCollectInterval < 0f)
            {
                written += Add(results, written, maxCount, nameof(m_MinGCCollectInterval),
                    "MinGCCollectInterval is {0}: a negative interval never throttles, so every collect request runs " +
                    "GC.Collect immediately.", m_MinGCCollectInterval);
            }

            // 非正数的过期预算：ProcessResourceMaintenance 以 expireBudget > 0 为闸门，0 就是时间轮不推进。
            if (m_ExpireProcessCountPerFrame <= 0)
            {
                written += Add(results, written, maxCount, nameof(m_ExpireProcessCountPerFrame),
                    "ExpireProcessCountPerFrame is {0}: the expiry wheels only advance on a positive budget, so idle and " +
                    "keep-alive records are never swept. Keep it at or above 1.", m_ExpireProcessCountPerFrame);
            }

            return written;
        }

        /// <summary>
        /// 打印配置问题并返回条数，供启动期与构建期各调一次。
        /// <para>缓冲复用：启动与构建各跑一趟，不为 8 个槽位反复分配。</para>
        /// </summary>
        internal int ReportConfigurationIssues()
        {
            if (s_IssueBuffer == null)
            {
                s_IssueBuffer = new ResourceSettingsIssue[8];
            }

            int count = GetConfigurationIssues(s_IssueBuffer, s_IssueBuffer.Length);
            for (int i = 0; i < count; i++)
            {
                LogUtility.Warning("[ResourceSettings] {0}", s_IssueBuffer[i].Detail);
            }

            return count;
        }

        private static ResourceSettingsIssue[] s_IssueBuffer;

        private static int Add(ResourceSettingsIssue[] results, int written, int maxCount, string field,
            string format, params object[] args)
        {
            if (written >= maxCount || written >= results.Length)
            {
                return 0;
            }

            // 冷路径格式化；params 仅在命中判据时产生，正常配置零开销。
            var issue = new ResourceSettingsIssue
            {
                Field = field,
                Detail = args == null || args.Length == 0 ? format : string.Format(format, args),
            };
            results[written] = issue;
            return 1;
        }
    }
}
