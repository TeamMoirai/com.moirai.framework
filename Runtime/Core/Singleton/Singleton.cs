using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 全局对象必须继承于此。
    /// </summary>
    /// <typeparam name="T">子类类型。</typeparam>
    public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
    {
        private static T s_Instance = null;
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object s_Locker = new object();

        public static T Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    lock (s_Locker)
                    {
                        // 双重检查锁（Double-Check Locking）
                        if (s_Instance == null)
                        {
                            s_Instance = new T();
                            s_Instance.OnInit();

                            if (Application.isPlaying)
                            {
                                SingletonSystem.Retain(s_Instance);
                            }
                        }
                    }
                }

                return s_Instance;
            }
        }

        public static bool IsValid => s_Instance != null;

        protected Singleton()
        {
#if UNITY_EDITOR
            string st = new StackTrace().ToString();
            // using const string to compare simply
            if (!st.Contains($"{typeof(Singleton<T>).Namespace}.Singleton`1[T].get_Instance"))
            {
                UnityEngine.Debug.LogError($"请必须通过Instance方法来实例化 {typeof(T).FullName} 类");
            }
#endif
        }

        protected virtual void OnInit() { }

        public void Active()
        {
            OnActive();
        }

        protected virtual void OnActive() { }

        public void Release()
        {
            Shutdown();
            if (s_Instance != null)
            {
                SingletonSystem.Release(s_Instance);
                s_Instance = null;
            }
        }
        
        protected virtual void Shutdown() { }
    }
}