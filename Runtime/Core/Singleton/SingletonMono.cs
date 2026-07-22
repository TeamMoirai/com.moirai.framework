using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 单例。简单实例化当前 Component。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public abstract class SingletonMono<T> : MonoBehaviour where T : SingletonMono<T>
    {
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

        protected static T s_Instance;
        protected static readonly object s_Locker = new object();
        protected static bool s_ShuttingDown;

        protected float _initializationTime; // 初始化此单例的时间戳

        /// <summary>
        /// 此单例是否已有实例
        /// </summary>
        public static bool HasInstance => s_Instance != null && !s_ShuttingDown;
        public static T TryGetInstance() => HasInstance ? s_Instance : null;
        public static T Current => Instance;
        
		/// <summary>
        /// 单例设计模式
        /// </summary>
        /// <value>实例</value>
        public static T Instance
        {
            get
            {
                if (s_ShuttingDown) return null;

                if (s_Instance == null)
                {
                    lock (s_Locker)
                    {
                        System.Type thisType = typeof(T);
                        string instName = thisType.Name;
                        GameObject go = SingletonSystem.GetGameObject(instName);
                        if (go == null)
                        {
                            if (MainThreadDispatcher.IsMainThread)
                            {
                                s_Instance = UnityUtility.FindObjectByType<T>();
                                if (!Application.isPlaying) return s_Instance;
                            }
                            
                            // 双重检查锁（Double-Check Locking）
                            if (s_Instance == null)
                            {
	                            go = new GameObject($"[{instName}]_AutoCreated");
	                            s_Instance = go.AddComponent<T>();
                            }
                        }
                        else
                        {
	                        go.name = $"[{instName}]";
                        }
                        
                        if (go != null)
                        {
                            s_Instance = go.GetComponent<T>();
                            if (s_Instance == null)
                            {
                                s_Instance = go.AddComponent<T>();
                            }
                        }

                        if (s_Instance == null)
                        {
	                        Log.Fatal($"SingletonBehaviour<{thisType}> creation failed | IsMainThread:{MainThreadDispatcher.IsMainThread}");
                        }

                        if (Application.isPlaying)
                        {
	                        SingletonSystem.Retain(s_Instance.gameObject, instName, s_Instance);
                        }
                    }
                }
                return s_Instance;
            }
        }
        
        /// <summary>
        /// Awake()时，初始化单例与其他配置
        /// </summary>
        protected void Awake()
        {
	        if (s_ShuttingDown) return;

	        CheckMultipleInstance();

	        if (s_Instance == this)
	        {
		        s_ShuttingDown = false;
		        OnInit();
	        }
        }
        
        /// <summary>
        /// 检查是否有多个实例
        /// </summary>
        /// <returns>当前实例是否为单例</returns>
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
	        SingletonSystem.Retain(gameObject, typeof(T).Name, s_Instance);
        }

        /// <summary>
        /// 初始化其他配置
        /// </summary>
        protected virtual void OnInit() { }

        protected void OnDestroy()
        {
	        if (s_Instance != this) return;

	        s_ShuttingDown = true;

	        SingletonSystem.Release(s_Instance.gameObject, typeof(T).Name, s_Instance);
	        s_Instance = null;
	        Shutdown();

	        // 如果是应用退出（编辑器停止或程序关闭），保持 true，彻底阻止重新创建。
	        if (Application.isPlaying) s_ShuttingDown = false;
        }
        
        /// <summary>
        /// 释放单例
        /// </summary>
        protected virtual void Shutdown() { }
        
        protected void OnApplicationQuit()
        {
	        s_ShuttingDown = true;
        }
    }
}