using System;
using System.Diagnostics;

namespace Moirai.Atropos
{
    /// <summary>
    /// 纯 C# 类单例基类（无 Unity 依赖，线程安全）。
    /// </summary>
    /// <typeparam name="T">继承本基类的具体单例类型。</typeparam>
    /// <remarks>
    /// <para><b>线程模型</b>：任意线程可安全访问 <see cref="Instance"/>。
    /// 惰性创建采用 volatile 读快速路径 + 双检锁（Double-Checked Locking），
    /// 快速路径仅一次原子读，不产生锁开销与分配。</para>
    /// <para><b>初始化契约</b>：实例构造后<b>先发布后初始化</b>——<see cref="OnInit"/>
    /// 在锁内、于实例已写入 <see cref="s_Instance"/> 之后执行：
    /// OnInit 内同线程递归访问 <see cref="Instance"/> 会取回正在初始化中的同一实例；
    /// 跨线程首次访问则阻塞至初始化完成后再返回。</para>
    /// <para><b>释放契约</b>：<see cref="Dispose"/> 幂等——仅当前活动实例能触发
    /// <see cref="OnShutdown"/> 并清空静态引用；对已释放/已被替换的陈旧实例调用为 no-op，
    /// 不会误杀当前活动实例。</para>
    /// <para><b>域重载</b>：关闭 Domain Reload 的编辑器工作流中，静态实例会跨播放会话存活，
    /// 派生类如需会话间重置，应参照 MainThreadDispatcher 模式自行提供 Reset 钩子
    /// （基类无法为泛型派生类自动注册 <c>RuntimeInitializeOnLoadMethod</c>）。</para>
    /// </remarks>
    public abstract class Singleton<T> : IDisposable where T : Singleton<T>, new()
    {
        #region 静态状态 [Static State]

        /// <summary>当前单例实例（volatile：后台线程经 <see cref="Instance"/> 访问的原子快速路径）。</summary>
        private static volatile T s_Instance;

        /// <summary>创建/释放互斥锁（每个封闭泛型类型独立一份）。</summary>
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object s_Locker = new object();

#if UNITY_EDITOR
        /// <summary>合法构造调用方的栈帧签名（编辑器守卫用，静态缓存避免每次构造重复分配）。</summary>
        private static readonly string s_InstanceGetterName = StringUtility.Format("{0}.Singleton`1[T].get_Instance", typeof(Singleton<T>).Namespace);
#endif

        #endregion

        #region 单例访问 [Singleton Access]

        /// <summary>
        /// 获取单例实例；首次访问时惰性创建并初始化（线程安全）。
        /// </summary>
        public static T Instance
        {
            get
            {
                T instance = s_Instance; // volatile 读：已物化时的无锁快速路径
                if (instance != null) return instance;

                lock (s_Locker)
                {
                    // 双检锁：入锁后重读，防止等待期间他线程已完成创建
                    instance = s_Instance;
                    if (instance != null) return instance;

                    instance = new T();
                    // 先发布后初始化：OnInit 内递归访问 Instance 取回同一实例（见类备注）
                    s_Instance = instance;
                    instance.OnInit();
                    return instance;
                }
            }
        }

        /// <summary>
        /// 单例当前是否已创建（不触发创建）。
        /// </summary>
        public static bool IsValid => s_Instance != null;

        #endregion

        #region 生命周期 [Lifecycle]

        /// <summary>
        /// 构造函数（编辑器环境下校验调用方，见 <see cref="ValidateConstructionContext"/>）。
        /// </summary>
        protected Singleton()
        {
#if UNITY_EDITOR
            ValidateConstructionContext();
#endif
        }

        /// <summary>
        /// 实例创建后的初始化回调（在锁内、实例发布之后执行，应保持轻量）。
        /// </summary>
        protected virtual void OnInit() { }

        /// <summary>
        /// 释放当前单例：回调 <see cref="OnShutdown"/> 并清空静态引用（幂等）。
        /// </summary>
        /// <remarks>
        /// <para>仅当前活动实例（<c>s_Instance == this</c>）可被释放；对陈旧实例或已释放实例重复调用为 no-op。
        /// 释放后再次访问 <see cref="Instance"/> 将创建新实例并重新初始化。</para>
        /// <para><see cref="OnShutdown"/> 在锁内执行且先于引用清空——期间访问 <see cref="Instance"/>
        /// 仍取回正在关闭中的当前实例，不会创建替身。</para>
        /// </remarks>
        public void Dispose()
        {
            lock (s_Locker)
            {
                // 幂等守卫：陈旧实例/重复释放不误杀当前活动实例
                if (!ReferenceEquals(s_Instance, this)) return;

                OnShutdown();
                s_Instance = null;
            }
        }

        /// <summary>
        /// 释放前的关闭回调（在锁内执行，应保持轻量）。
        /// </summary>
        protected virtual void OnShutdown() { }

        #endregion

        #region 编辑器守卫 [Editor Validation]

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器环境校验构造调用方：<c>new()</c> 约束要求公共构造函数，
        /// 此守卫在直接 <c>new</c> 派生类时立即告警，而非产生游离于单例体系外的实例。
        /// </summary>
        /// <remarks>
        /// 构造链可能穿越多层派生类构造函数，故对整个堆栈做签名包含匹配而非固定帧深度；
        /// 仅编辑器生效，运行时零开销。
        /// </remarks>
        private void ValidateConstructionContext()
        {
            string stackTrace = new StackTrace().ToString();
            if (!stackTrace.Contains(s_InstanceGetterName))
            {
                LogUtility.Error("必须通过 {0}.Instance 访问单例，禁止直接构造", typeof(T).FullName);
            }
        }
#endif

        #endregion
    }
}
