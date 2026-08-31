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
    [DisallowMultipleComponent]
    public sealed class MemoryPoolSetting : MonoBehaviour
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

        private EMemoryPoolPhase _previousPhase = EMemoryPoolPhase.Gameplay;

        #endregion

        #region 生命周期 [LIFECYCLE]

        private void OnValidate()
        {
            NormalizeSettings();
        }

        private void OnEnable()
        {
            Application.lowMemory += OnLowMemory;
            Application.focusChanged += OnFocusChanged;
        }

        private void OnDisable()
        {
            Application.lowMemory -= OnLowMemory;
            Application.focusChanged -= OnFocusChanged;
        }

        private void OnLowMemory()
        {
            _previousPhase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;
            MemoryPoolRegistry.CompactAll();
            MemoryPoolRegistry.Phase = _previousPhase;
        }

        private void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                MemoryPoolRegistry.Phase = _previousPhase;
            }
            else
            {
                _previousPhase = MemoryPoolRegistry.Phase;
                MemoryPoolRegistry.Phase = EMemoryPoolPhase.Background;
            }
        }

        private void Awake()
        {
            NormalizeSettings();
            MemoryPoolRegistry.InitializeMainThread();
            MemoryPool.ShortDecayStartFrames = m_ShortDecayStartFrames;
            MemoryPool.LongDecayStartFrames = m_LongDecayStartFrames;
            MemoryPool.UnscheduleIdleFrames = m_UnscheduleIdleFrames;
            MemoryPool.ZeroFreeReserveStartFrames = m_ZeroFreeReserveStartFrames;
            MemoryPool.AutoTrimNativeMetadataFrames = m_AutoTrimNativeMetadataFrames;
            MemoryPool.SetDefaultCapacity(m_SoftFreeReserveLimit, m_HardFreeReserveLimit);
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Boot;
        }

        private void Update()
        {
            MemoryPoolRegistry.TickAll(Time.frameCount);
        }

        private void OnDestroy()
        {
            MemoryPoolRegistry.TrimAllNativeMetadata();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void NormalizeSettings()
        {
            m_ShortDecayStartFrames = Mathf.Max(0, m_ShortDecayStartFrames);
            m_LongDecayStartFrames = Mathf.Max(m_ShortDecayStartFrames, m_LongDecayStartFrames);
            m_ZeroFreeReserveStartFrames = Mathf.Max(m_LongDecayStartFrames, m_ZeroFreeReserveStartFrames);
            m_UnscheduleIdleFrames = Mathf.Max(m_ZeroFreeReserveStartFrames, m_UnscheduleIdleFrames);
            m_AutoTrimNativeMetadataFrames = m_AutoTrimNativeMetadataFrames < 0
                ? -1
                : Mathf.Max(m_ZeroFreeReserveStartFrames, m_AutoTrimNativeMetadataFrames);
            m_SoftFreeReserveLimit = Mathf.Max(MemoryPool.MINIMUM_FREE_RESERVE_LIMIT, m_SoftFreeReserveLimit);
            m_HardFreeReserveLimit = Mathf.Max(m_SoftFreeReserveLimit, m_HardFreeReserveLimit);
        }

        #endregion
    }
}
