using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 单例基类：首次访问时查找场景已有实例，未找到则在主线程自动创建。
    /// </summary>
    /// <typeparam name="T">继承本基类的具体单例类型。</typeparam>
    /// <remarks>
    /// <para><b>线程模型</b>：实例物化后，任意线程访问 <see cref="Instance"/> 只命中
    /// volatile 读原子快速路径；<b>物化（查找/创建）只能发生在主线程</b>——后台线程在实例
    /// 尚未物化时访问将抛出 <see cref="GameException"/>（替代越线程调用 Unity API 的未定义行为）。
    /// 需要后台线程访问的派生类应在启动阶段于主线程预热（参照 MainThreadDispatcher.BootstrapOnPlay）。</para>
    /// <para><b>编辑模式</b>：仅查找已有实例、不自动创建（避免向场景写入瞬时对象），
    /// 未找到时返回 null。</para>
    /// <para><b>退出窗口</b>：应用退出/播放停止期间 <see cref="Instance"/> 返回 null 且拒绝重新创建，
    /// 防止退出期复活单例；<see cref="IsValid"/> 与 <see cref="TryGetInstance"/> 同步反映该状态。</para>
    /// <para><b>多实例策略</b>：场景中已存在实例时，默认销毁新实例（先到先得）；
    /// 勾选 <see cref="m_Replaceable"/> 后改为最新实例胜出（适用于局部需要重建的单例，eg：背景音乐）。</para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public abstract class SingletonMono<T> : MonoBehaviour where T : SingletonMono<T>
    {
        #region 序列化配置 [Serialized Configuration]

        /// <!-- 单例 -->
        private const string SINGLETON_GROUP = "单例 [Singleton]";

        [BoxGroup(SINGLETON_GROUP)]
        [Tooltip("是否为持久单例。\n适用于全局脚本，eg：配置管理器。")]
        [DisableInPlayMode]
        [SerializeField] protected bool m_Persistent;

        [BoxGroup(SINGLETON_GROUP)]
        [Tooltip("最新创建的实例作为单例，销毁旧实例。\n适用于局部有更新的单例，eg：背景音乐")]
        [DisableInPlayMode]
        [SerializeField] protected bool m_Replaceable;

        #endregion

        #region 静态状态 [Static State]

        /// <summary>当前单例实例（volatile：后台线程访问的原子快速路径）。</summary>
        protected static volatile T s_Instance;

        /// <summary>物化互斥锁（每个封闭泛型类型独立一份）。</summary>
        // ReSharper disable once StaticMemberInGenericType
        protected static readonly object s_Locker = new object();

        /// <summary>退出窗口标记：应用退出/播放停止期间为 true，Instance 拒绝物化并返回 null。</summary>
        protected static volatile bool s_ShuttingDown;

        /// <summary>初始化此单例的时间戳（多实例竞争时的仲裁依据）。</summary>
        protected float _initializationTime;

        #endregion

        #region 单例访问 [Singleton Access]

        /// <summary>
        /// 此单例是否已有可用实例（不含退出窗口期）。
        /// </summary>
        public static bool IsValid => s_Instance != null && !s_ShuttingDown;

        /// <summary>
        /// 获取单例实例；无实例或处于退出窗口时返回 null（不抛出）。
        /// </summary>
        public static T TryGetInstance() => IsValid ? s_Instance : null;

        /// <summary>
        /// 获取单例实例（<see cref="Instance"/> 的别名，供语义化调用点使用）。
        /// </summary>
        public static T Current => Instance;

        /// <summary>
        /// 单例设计模式：获取实例；首次访问时查找场景已有实例，未找到则自动创建。
        /// </summary>
        /// <value>实例；退出窗口期或编辑模式未找到时为 null。</value>
        public static T Instance
        {
            get
            {
                // 退出窗口：拒绝物化（返回 null 而非抛出，退出期第三方代码仍可能取值）
                if (s_ShuttingDown) return null;

                T instance = s_Instance; // volatile 读：已物化时的无锁快速路径
                if (instance != null) return instance;

                lock (s_Locker)
                {
                    // 入锁重检：等待期间可能已进入退出窗口或他线程已完成物化
                    if (s_ShuttingDown) return null;

                    instance = s_Instance;
                    if (instance != null) return instance;

                    // 物化（Unity API）只能发生在主线程：越线程调用属于未定义行为，fail-fast 显式化
                    if (!MainThreadDispatcher.IsMainThread)
                    {
                        throw new GameException(StringUtility.Format(
                            "SingletonMono<{0}> 的实例物化必须发生在主线程：后台线程在物化前的访问不受支持，" +
                            "请先在启动阶段于主线程访问一次 Instance（参照 MainThreadDispatcher.BootstrapOnPlay 预热模式）。",
                            typeof(T).FullName));
                    }

                    instance = UnityUtility.FindObjectByType<T>();

                    // 编辑模式只查找不创建，避免向场景写入瞬时对象（未找到返回 null）
                    if (!Application.isPlaying)
                    {
                        s_Instance = instance;
                        return instance;
                    }

                    // 双检锁：查找未命中时创建新实例
                    if (instance == null)
                    {
                        GameObject go = new GameObject(StringUtility.Format("[{0}]_AutoCreated", typeof(T).Name));
                        // AddComponent 同步触发 Awake → CheckMultipleInstance 赋值 s_Instance 并回调 OnInit
                        instance = go.AddComponent<T>();
                    }

                    if (instance == null)
                    {
                        // 仅记录不抛出：显式跳过后续赋值，避免空实例引发 NRE
                        LogUtility.Fatal("SingletonMono<{0}> creation failed", typeof(T));
                        return null;
                    }

                    s_Instance = instance;
                    return instance;
                }
            }
        }

        #endregion

        #region 生命周期 [Lifecycle]

        /// <summary>
        /// Awake() 时初始化单例与其他配置（非虚：派生类扩展初始化请覆写 <see cref="OnInit"/>）。
        /// </summary>
        protected void Awake()
        {
            if (s_ShuttingDown) return;

            CheckMultipleInstance();

            if (s_Instance == this)
            {
                // 防御性清除遗留的退出标记（物化竞态/关 Domain Reload 工作流）并执行初始化
                s_ShuttingDown = false;
                OnInit();
            }
        }

        /// <summary>
        /// 检查是否有多个实例并按策略消解（先到先得 / 最新胜出），随后登记自身为单例。
        /// </summary>
        protected virtual void CheckMultipleInstance()
        {
            _initializationTime = Time.time;

            // 防止创建多余单例
            if (s_Instance)
            {
                if (!m_Replaceable || s_Instance._initializationTime < _initializationTime)
                {
                    Destroy(gameObject);
                    return;
                }

                Destroy(s_Instance.gameObject);
            }

            if (m_Persistent)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }

            s_Instance = this as T;
        }

        /// <summary>
        /// 初始化其他配置（实例胜出后回调，可覆写）。
        /// </summary>
        protected virtual void OnInit() { }

        /// <summary>
        /// 实例销毁时的单例清理（非虚：派生类扩展关闭逻辑请覆写 <see cref="OnShutdown"/>）。
        /// </summary>
        protected void OnDestroy()
        {
            if (s_Instance != this) return;

            s_ShuttingDown = true;

            s_Instance = null;
            OnShutdown();

            // 如果是应用退出（编辑器停止或程序关闭），保持 true，彻底阻止重新创建。
            if (Application.isPlaying) s_ShuttingDown = false;
        }

        /// <summary>
        /// 释放单例（销毁前回调，可覆写）。
        /// </summary>
        protected virtual void OnShutdown() { }

        /// <summary>
        /// 应用退出时进入退出窗口，阻止退出期重新创建。
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            s_ShuttingDown = true;
        }

        #endregion
    }
}
