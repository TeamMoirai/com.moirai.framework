using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 存储和检索已注册组件的静态注册表。
    /// </summary>
    /// <remarks>
    /// 使用此系统可提供比 FindObject 方法更快的结果，
    /// 并且非常适合需要由多个游戏对象引用的非单例组件。
    /// </remarks>
    public static class ToolRegistry
    {
        #region 内部结构 [INTERNAL]

        private struct RegistryEntry
        {
            public string Key { get; }
            public object Object { get; }
            public bool Persist { get; }
            public UnityEngine.SceneManagement.Scene Scene { get; }

            public RegistryEntry(string key, object obj, bool persist,
                UnityEngine.SceneManagement.Scene scene)
            {
                Key = key;
                Object = obj;
                Persist = persist;
                Scene = scene;
            }
        }

        #endregion

        #region 变量 [VARIABLES]

        private static readonly List<RegistryEntry> s_Entries = new List<RegistryEntry>();
        private static int s_NullCount;

        #endregion

        #region 生命周期 [LIFECYCLE]

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        public static void InitializeOnEnterPlayMode()
        {
            lock (s_Entries)
            {
                s_Entries.Clear();
                s_NullCount = 0;
            }
        }
#endif

        static ToolRegistry()
        {
            if (Application.isPlaying)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
        }

        #endregion

        #region 清除 [CLEAR]

        /// <summary>
        /// 清除所有已注册的组件。
        /// </summary>
        public static void Clear()
        {
            lock (s_Entries)
            {
                s_Entries.Clear();
                s_NullCount = 0;
            }
        }

        #endregion

        #region 查找 [GET]

        /// <summary>
        /// 获取第一个匹配类型的已注册组件。
        /// </summary>
        public static T GetComponent<T>()
        {
            lock (s_Entries)
            {
                CompactIfNeeded();

                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object is T t)
                    {
                        return t;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 获取第一个匹配类型的已注册组件。
        /// </summary>
        public static object GetComponent(Type type)
        {
            if (type == null) return null;

            lock (s_Entries)
            {
                CompactIfNeeded();

                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (type.IsInstanceOfType(s_Entries[i].Object))
                    {
                        return s_Entries[i].Object;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 使用键获取已注册的组件。
        /// 同一 key 可注册多个不同类型的组件（一对多语义）。
        /// </summary>
        /// <param name="key">要查找的键</param>
        /// <param name="entityFallback">
        /// 未找到直接匹配时，尝试从键对应的 GameObject 上按类型查找子组件
        /// </param>
        public static T GetComponent<T>(string key, bool entityFallback = false)
        {
            if (string.IsNullOrEmpty(key))
            {
                return GetComponent<T>();
            }

            lock (s_Entries)
            {
                CompactIfNeeded();

                // 1) 直接类型匹配
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Key == key && s_Entries[i].Object is T direct)
                    {
                        return direct;
                    }
                }

                if (!entityFallback) return default;

                // 2) Fallback：从键对应的 GameObject 上查找子组件
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Key == key && s_Entries[i].Object is GameObject go)
                    {
                        T fallback = go.GetComponentInChildren<T>();
                        if (fallback != null) return fallback;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 获取所有匹配类型的已注册组件（每次调用分配新 List）。
        /// </summary>
        public static List<T> GetComponents<T>()
        {
            List<T> results = new List<T>();
            GetComponents(results);
            return results;
        }

        /// <summary>
        /// 获取所有匹配类型的已注册组件（零 GC 版本）。
        /// </summary>
        /// <param name="results">用于接收结果的列表，调用前无需清空</param>
        public static void GetComponents<T>(List<T> results)
        {
            results.Clear();

            lock (s_Entries)
            {
                CompactIfNeeded();

                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object is T t)
                    {
                        results.Add(t);
                    }
                }
            }
        }

        #endregion

        #region 注册 [REGISTER]

        /// <summary>
        /// 将组件添加到注册表。
        /// </summary>
        /// <param name="component">要注册的组件</param>
        /// <param name="persistBetweenScenes">在场景之间保持注册</param>
        public static void RegisterComponent(object component, bool persistBetweenScenes = false)
        {
            RegisterComponent(component, null, persistBetweenScenes);
        }

        /// <summary>
        /// 将组件添加到注册表。
        /// 同一组件可使用不同 key 重复注册（key 可为 null）。
        /// </summary>
        /// <param name="component">要注册的组件</param>
        /// <param name="key">与组件关联的键（可为 null，null 表示仅按类型查找）</param>
        /// <param name="persistBetweenScenes">在场景之间保持注册</param>
        public static void RegisterComponent(object component, string key, bool persistBetweenScenes = false)
        {
            if (component == null) return;

            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (ReferenceEquals(s_Entries[i].Object, component)
                        && s_Entries[i].Key == key)
                    {
                        Log.Warning($"Already registered! [{key ?? "null"}]: {component.GetType().Name}");
                        return;
                    }
                }

                // Log.Info($"Register[{key}]: {component.GetType().Name}");
                s_Entries.Add(new RegistryEntry(key, component, persistBetweenScenes, SceneManager.GetActiveScene()));
            }
        }

        #endregion

        #region 移除 [REMOVE]

        /// <summary>
        /// 从注册表中删除指定组件的所有条目（同一组件可注册多次）。
        /// </summary>
        public static void RemoveComponent(object component)
        {
            if (component == null) return;

            lock (s_Entries)
            {
                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(s_Entries[i].Object, component))
                    {
                        RemoveEntryAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// 从注册表中删除指定组件和键的条目。
        /// </summary>
        public static void RemoveComponent(object component, string key)
        {
            if (component == null) return;

            lock (s_Entries)
            {
                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(s_Entries[i].Object, component)
                        && s_Entries[i].Key == key)
                    {
                        RemoveEntryAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// 从注册表中删除指定键的所有组件。
        /// </summary>
        public static void RemoveComponentsByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            lock (s_Entries)
            {
                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    if (s_Entries[i].Key == key)
                    {
                        RemoveEntryAt(i);
                    }
                }
            }
        }

        #endregion

        #region 场景回调 [SCENE CALLBACKS]

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            LoadSceneMode mode)
        {
            lock (s_Entries)
            {
                UnityEngine.SceneManagement.Scene currentScene = SceneManager.GetActiveScene();
                int writeIndex = 0;

                for (int i = 0; i < s_Entries.Count; i++)
                {
                    var entry = s_Entries[i];

                    // 丢弃已销毁的对象（包括 Unity fake-null）
                    if (entry.Object == null)
                    {
                        continue;
                    }

                    // Single 模式：丢弃非持久 + 非当前场景的条目
                    // Additive 模式：保留所有存活条目（仅清理已销毁的）
                    bool keep = mode == LoadSceneMode.Additive
                             || entry.Persist
                             || entry.Scene == currentScene;

                    if (keep)
                    {
                        s_Entries[writeIndex++] = entry;
                    }
                }

                if (writeIndex < s_Entries.Count)
                {
                    s_Entries.RemoveRange(writeIndex, s_Entries.Count - writeIndex);
                }

                s_NullCount = 0;
            }
        }

        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            lock (s_Entries)
            {
                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    var entry = s_Entries[i];

                    if (!entry.Persist && (entry.Scene == scene || entry.Object == null))
                    {
                        RemoveEntryAt(i);
                    }
                }
            }
        }

        #endregion

        #region 内部方法 [INTERNAL]

        /// <summary>
        /// O(1) 移除：与末尾交换后移除。
        /// 同时处理循环中多处调用时可能产生的"跳过"问题（交换过来的元素仍需检查）。
        /// </summary>
        private static void RemoveEntryAt(int index)
        {
            int last = s_Entries.Count - 1;

            if (index < last)
            {
                // 交换后，原末尾元素来到 index 位置；
                // 由于外层循环是逆序 (i--)，下一轮会检查到它，不会跳过
                s_Entries[index] = s_Entries[last];
            }

            s_Entries.RemoveAt(last);
        }

        /// <summary>
        /// 当 Unity "fake null" 条目积累到阈值时，就地压缩列表。
        /// 调用前必须持有 s_Entries 锁。
        /// </summary>
        private static void CompactIfNeeded()
        {
            if (s_NullCount < 32) return;

            int writeIndex = 0;

            for (int i = 0; i < s_Entries.Count; i++)
            {
                if (s_Entries[i].Object != null)
                {
                    s_Entries[writeIndex++] = s_Entries[i];
                }
            }

            if (writeIndex < s_Entries.Count)
            {
                s_Entries.RemoveRange(writeIndex, s_Entries.Count - writeIndex);
            }

            s_NullCount = 0;
        }

        #endregion
    }
}