using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 游戏对象池设置组件。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(MoiraiExecutionOrder.GAME_APP_ORDER)]
    public sealed class ObjectPoolSetting : MonoBehaviour
    {
        #region 序列化字段 [SERIALIZED FIELDS]

        [Header("池配置 [Pool Config]")]
        [Tooltip("池配置 ScriptableObject。为空时使用空配置（所有地址都会警告未注册）。")]
        [SerializeField] private PoolConfigScriptableObject m_PoolConfig;

        [Header("预热设置 [Warmup Settings]")]
        [Tooltip("预热时每帧最大创建数量。")]
        [SerializeField] private int m_WarmupBatchSize = 8;

        [Tooltip("预热时每帧时间预算（秒），超出则让出一帧。")]
        [SerializeField] private float m_WarmupFrameBudgetSeconds = 0.001f;

        [Header("默认策略 [Default Policy]")]
        [Tooltip("默认池策略。")]
        [SerializeField] private EPoolPolicy m_DefaultPolicy = EPoolPolicy.Burst;

        [Tooltip("默认最小空闲数量。")]
        [SerializeField] private int m_DefaultMinIdle = 0;

        [Tooltip("默认软容量。")]
        [SerializeField] private int m_DefaultSoftCapacity = 8;

        [Tooltip("默认硬容量。")]
        [SerializeField] private int m_DefaultHardCapacity = 16;

        [Tooltip("默认空闲超时秒数。")]
        [SerializeField] private float m_DefaultIdleSeconds = 15f;

        [Tooltip("默认是否在空闲时卸载预制体。")]
        [SerializeField] private bool m_DefaultUnloadPrefab = true;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取池配置。
        /// </summary>
        public PoolConfigScriptableObject PoolConfig => m_PoolConfig;

        /// <summary>
        /// 获取预热每帧批量大小。
        /// </summary>
        public int WarmupBatchSize => m_WarmupBatchSize;

        /// <summary>
        /// 获取预热帧预算（秒）。
        /// </summary>
        public float WarmupFrameBudgetSeconds => m_WarmupFrameBudgetSeconds;

        #endregion

        #region 生命周期 [LIFECYCLE]

        private void OnValidate()
        {
            NormalizeSettings();
        }

        private void Awake()
        {
            NormalizeSettings();

            // 将配置注入到服务实例。
            ObjectPoolService.LoadCatalog(m_PoolConfig);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void NormalizeSettings()
        {
            m_WarmupBatchSize = Mathf.Max(1, m_WarmupBatchSize);
            m_WarmupFrameBudgetSeconds = Mathf.Max(0.0001f, m_WarmupFrameBudgetSeconds);
            m_DefaultMinIdle = Mathf.Max(0, m_DefaultMinIdle);
            m_DefaultSoftCapacity = Mathf.Max(1, m_DefaultSoftCapacity);
            m_DefaultHardCapacity = Mathf.Max(m_DefaultSoftCapacity, m_DefaultHardCapacity);
            m_DefaultIdleSeconds = Mathf.Max(0f, m_DefaultIdleSeconds);
        }

        #endregion
    }
}
