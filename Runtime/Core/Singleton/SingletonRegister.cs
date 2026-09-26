namespace Moirai.Atropos
{
    /// <summary>
    /// 注册式纯 C# 单例：无需继承基类，任意含公共无参构造的类型均可注册为单例。
    /// </summary>
    /// <typeparam name="T">注册为单例的类型。</typeparam>
    /// <remarks>
    /// 与 <see cref="Singleton{T}"/>（CRTS 自约束）互补：适合无法改继承关系的既有类型。
    /// 线程安全（volatile 读快速路径 + 双检锁），但不提供生命周期回调与释放语义。
    /// </remarks>
    public class SingletonRegister<T> where T : class, new()
    {
        #region 静态状态 [Static State]

        /// <summary>当前单例实例（volatile：快速路径原子读）。</summary>
        private static volatile T s_Instance;

        /// <summary>创建互斥锁（每个封闭泛型类型独立一份）。</summary>
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object s_Locker = new object();

        #endregion

        #region 单例访问 [Singleton Access]

        /// <summary>
        /// 获取单例实例；首次访问时惰性创建（线程安全）。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;

                lock (s_Locker)
                {
                    // 双检锁：入锁后重读，防止等待期间他线程已完成创建
                    s_Instance ??= new T();
                }

                return s_Instance;
            }
        }

        #endregion
    }
}
