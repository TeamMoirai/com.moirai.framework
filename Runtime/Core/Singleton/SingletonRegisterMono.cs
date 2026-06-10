using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// Mono单例的另一种实现方式，可以将所有Mono注册成单例而无需继承
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
    public static class SingletonRegisterMono<T> where T : MonoBehaviour
    {
        private static T m_Instance;
        private static readonly object m_Locker = new object();

        public static T Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    lock (m_Locker)
                    {
                        if (m_Instance == null)
                        {
                            GameObject obj = new GameObject(typeof(T).Name + "_AutoCreated");
                            // obj.hideFlags = HideFlags.HideAndDontSave;
                            m_Instance = obj.AddComponent<T>();
                        }
                    }
                }

                return m_Instance;
            }
        }
    }
}