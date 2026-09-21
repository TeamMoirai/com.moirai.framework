using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池阶段。
    /// </summary>
    public enum EMemoryPoolPhase : byte
    {
        /// <summary>
        /// 启动阶段。
        /// </summary>
        Boot = 0,

        /// <summary>
        /// 加载阶段。
        /// </summary>
        Loading,

        /// <summary>
        /// 游戏阶段。
        /// </summary>
        Gameplay,

        /// <summary>
        /// 后台阶段。
        /// </summary>
        Background,

        /// <summary>
        /// 低内存阶段。
        /// </summary>
        LowMemory
    }

    /// <summary>
    /// 内存池服务。
    /// </summary>
    [FrameworkSetting("[框架]内存池配置", "空闲修剪、容量设置", -401)]
    public sealed class MemoryPoolSetting : FrameworkSettings<MemoryPoolSetting>
    {
        #region 序列化字段 [SERIALIZED FIELDS]

        [Header("空闲修剪设置 [Idle Trim Settings]")]
        [Tooltip("池空闲多少帧后开始衰减目标空闲水位。每 tick 驱逐数量由 Phase 预算决定（Gameplay=2）。@60fps: 1800帧≈30秒")]
        [SerializeField] private int m_ShortDecayStartFrames = 1800;

        [Tooltip("池空闲多少帧后加速衰减目标空闲水位。每 tick 驱逐数量由 Phase 预算决定（Background=16, LowMemory=32）。@60fps: 7200帧≈2分钟")]
        [SerializeField] private int m_LongDecayStartFrames = 7200;

        [Tooltip("池空闲多少帧后停止调度 Tick。@60fps: 18000帧≈5分钟")]
        [SerializeField] private int m_UnscheduleIdleFrames = 18000;

        [Tooltip("池空闲多少帧后允许目标空闲缓存降为 0。@60fps: 7200帧≈2分钟")]
        [SerializeField] private int m_ZeroFreeReserveStartFrames = 7200;

        [Tooltip("池空闲多少帧后，若已完全空闲则自动释放 Native 元数据。@60fps: 18000帧≈5分钟")]
        [SerializeField] private int m_AutoTrimNativeMetadataFrames = 18000;

        [Header("容量设置 [Capacity Settings]")]
        [Tooltip("默认空闲缓存软上限。新池会使用该值，运行时修改会同步到已创建池。")]
        [SerializeField] private int m_SoftFreeReserveLimit = 128;

        [Tooltip("默认空闲缓存硬上限。释放对象时超过该值会直接驱逐。")]
        [SerializeField] private int m_HardFreeReserveLimit = 512;

        [Header("上线排查设置 [Release Diagnostics Settings]")]
        [Tooltip("正式构建也保留主线程守卫。编辑器与开发构建本来就一直在守卫下；\n" +
                 "QA / soak 包打开后跨线程取还会当场报错，而不是把非托管页元数据改坏后以随机崩溃回来。\n" +
                 "代价是每个取还动作多一次线程 id 比较，正式包建议关闭。")]
        [SerializeField] private bool m_VerifyMainThreadInRelease = false;

        [Tooltip("存活（在外）对象数量上限的全局默认值，0 表示不限制。\n" +
                 "硬上限只约束空闲缓存、不约束总量：漏还一只就永久少一只，表现为缓慢上涨的 OOM。\n" +
                 "开启后越界会带池身份限流上报，开发期直接抛出。")]
        [SerializeField] private int m_DefaultLiveLimit = 0;

        [NonSerialized] private EMemoryPoolPhase _previousPhase = EMemoryPoolPhase.Gameplay;

        #endregion

        #region 生命周期 [LIFECYCLE]

        private void OnValidate()
        {
            NormalizeSettings();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnInit()
        {
            NormalizeSettings();
            MemoryPool.ShortDecayStartFrames = Instance.m_ShortDecayStartFrames;
            MemoryPool.LongDecayStartFrames = Instance.m_LongDecayStartFrames;
            MemoryPool.UnscheduleIdleFrames = Instance.m_UnscheduleIdleFrames;
            MemoryPool.ZeroFreeReserveStartFrames = Instance.m_ZeroFreeReserveStartFrames;
            MemoryPool.AutoTrimNativeMetadataFrames = Instance.m_AutoTrimNativeMetadataFrames;
            MemoryPool.SetDefaultCapacity(Instance.m_SoftFreeReserveLimit, Instance.m_HardFreeReserveLimit);
            MemoryPool.VerifyMainThreadInRelease = Instance.m_VerifyMainThreadInRelease;
            MemoryPool.DefaultLiveLimit = Instance.m_DefaultLiveLimit;
            MemoryPoolRegistry.RefreshThreadGuard();
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Boot;

            Application.lowMemory += OnLowMemory;
            Application.focusChanged += OnFocusChanged;
            GameApp.AddDestroyListener(OnShutdown);

            GameApp.AddUpdateListener(OnUpdate);
            return;

            static void OnUpdate()
            {
                MemoryPoolRegistry.TickAll(Time.frameCount);
            }
        }

        private static void OnShutdown()
        {
            Application.lowMemory -= OnLowMemory;
            Application.focusChanged -= OnFocusChanged;

            MemoryPoolRegistry.ClearAll();
        }

        private static void OnLowMemory()
        {
            Instance._previousPhase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;
            MemoryPoolRegistry.CompactAll();
            MemoryPoolRegistry.Phase = Instance._previousPhase;
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                MemoryPoolRegistry.Phase = Instance._previousPhase;
            }
            else
            {
                Instance._previousPhase = MemoryPoolRegistry.Phase;
                MemoryPoolRegistry.Phase = EMemoryPoolPhase.Background;
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static void NormalizeSettings()
        {
            Instance.m_ShortDecayStartFrames = Mathf.Max(0, Instance.m_ShortDecayStartFrames);
            Instance.m_LongDecayStartFrames = Mathf.Max(Instance.m_ShortDecayStartFrames, Instance.m_LongDecayStartFrames);
            Instance.m_ZeroFreeReserveStartFrames = Mathf.Max(Instance.m_LongDecayStartFrames, Instance.m_ZeroFreeReserveStartFrames);
            Instance.m_UnscheduleIdleFrames = Mathf.Max(Instance.m_ZeroFreeReserveStartFrames, Instance.m_UnscheduleIdleFrames);
            Instance.m_AutoTrimNativeMetadataFrames = Instance.m_AutoTrimNativeMetadataFrames < 0
                ? -1
                : Mathf.Max(Instance.m_ZeroFreeReserveStartFrames, Instance.m_AutoTrimNativeMetadataFrames);
            Instance.m_SoftFreeReserveLimit = Mathf.Max(MemoryPool.MINIMUM_FREE_RESERVE_LIMIT, Instance.m_SoftFreeReserveLimit);
            Instance.m_HardFreeReserveLimit = Mathf.Max(Instance.m_SoftFreeReserveLimit, Instance.m_HardFreeReserveLimit);
            Instance.m_DefaultLiveLimit = Mathf.Max(0, Instance.m_DefaultLiveLimit);
        }

        #endregion
    }
}
