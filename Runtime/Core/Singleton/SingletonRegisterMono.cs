using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 注册式 MonoBehaviour 单例：可将任意 MonoBehaviour 类型注册为单例而无需继承。
    /// </summary>
    /// <example>
    /// 有以下 2 种用法：
    /// <code><![CDATA[
    /// public class TestMono : MonoBehaviour
    /// {
    ///     public void DoSomething(){};
    /// }
    /// 调用：SingletonRegisterMono<TestMono>.Instance.DoSomething();
    /// ]]></code>
    ///
    /// <code><![CDATA[
    /// public class TestSingletonMono : MonoBehaviour
    /// {
    ///     public static TestSingletonMono Instance { get => SingletonRegisterMono<TestSingletonMono>.Instance; }
    ///
    ///     public void DoSomething(){};
    /// }
    /// 调用：TestSingletonMono.Instance.DoSomething();
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// 与 <see cref="SingletonMono{T}"/> 互补：无场景查找、无多实例消解、无生命周期回调——
    /// 仅做「不存在则主线程新建 GameObject 挂载」的惰性物化。需要完整策略时请改用
    /// <see cref="SingletonMono{T}"/>。实例物化只能发生在主线程，越线程访问将抛出
    /// <see cref="GameException"/>。
    /// </remarks>
    public static class SingletonRegisterMono<T> where T : MonoBehaviour
    {
        #region 静态状态 [Static State]

        /// <summary>当前单例实例（volatile：快速路径原子读）。</summary>
        private static volatile T s_Instance;

        /// <summary>物化互斥锁（每个封闭泛型类型独立一份）。</summary>
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object s_Locker = new object();

        #endregion

        #region 单例访问 [Singleton Access]

        /// <summary>
        /// 获取单例实例；首次访问时在主线程自动创建宿主 GameObject 并挂载组件。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;

                lock (s_Locker)
                {
                    // 双检锁：入锁后重读，防止等待期间他线程已完成创建
                    if (s_Instance != null) return s_Instance;

                    // 物化（Unity API）只能发生在主线程：越线程调用属于未定义行为，fail-fast 显式化
                    if (!MainThreadDispatcher.IsMainThread)
                    {
                        throw new GameException(StringUtility.Format(
                            "SingletonRegisterMono<{0}> 的实例物化必须发生在主线程：后台线程在物化前的访问不受支持。",
                            typeof(T).FullName));
                    }

                    GameObject obj = new GameObject(StringUtility.Format("{0}_AutoCreated", typeof(T).Name));
                    // obj.hideFlags = HideFlags.HideAndDontSave;
                    s_Instance = obj.AddComponent<T>();
                }

                return s_Instance;
            }
        }

        #endregion
    }
}
