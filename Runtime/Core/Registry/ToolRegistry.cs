using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 存储和检索已注册组件的静态注册表。
    /// </summary>
    /// <remarks>使用此系统可提供比 FindObject 方法更快的结果，并且非常适合需要由多个游戏对象引用的非单例组件。</remarks>
    public static class ToolRegistry
    {
        private static readonly List<ToolRegistryEntry> s_Entries = new List<ToolRegistryEntry>();
        private static bool s_Subscribed;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        public static void InitializeOnEnterPlayMode()
        {
            s_Entries.Clear();
        }
#endif
        
        /// <summary>
        /// 清除所有已注册的组件
        /// </summary>
        public static void Clear()
        {
            lock (s_Entries)
            {
                s_Entries.Clear();
            }
        }

        /// <summary>
        /// 获取已注册的组件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>        
        public static T GetComponent<T>()
        {
            EnsureSceneAndList();

            lock (s_Entries)
            {
                ToolRegistryEntry[] search = s_Entries.ToArray();
                for (int i = 0; i < search.Length; i++)
                {
                    if (search[i] != null && search[i].@object != null && search[i].@object is T t)
                    {
                        return t;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 使用键获取已注册的组件
        /// </summary>
        /// <param name="key">要查找的键</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>        
        public static T GetComponent<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) return GetComponent<T>();

            EnsureSceneAndList();

            lock (s_Entries)
            {
                ToolRegistryEntry[] search = s_Entries.ToArray();
                for (int i = 0; i < search.Length; i++)
                {
                    if (search[i].@object is T t && search[i].key == key)
                    {
                        return t;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 获取已注册组件的列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> GetComponents<T>()
        {
            EnsureSceneAndList();

            List<T> results = new List<T>();
            lock (s_Entries)
            {
                ToolRegistryEntry[] search = s_Entries.ToArray();
                for (int i = 0; i < search.Length; i++)
                {
                    if (search[i].@object is T t)
                    {
                        results.Add(t);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 将组件添加到注册表
        /// </summary>
        /// <param name="component">要注册的组件</param>
        /// <param name="persistBetweenScenes">在场景之间保持组件注册</param>
        public static void RegisterComponent(object component, bool persistBetweenScenes = false)
        {
            RegisterComponent(component, null, persistBetweenScenes);
        }

        /// <summary>
        /// 将组件添加到注册表
        /// </summary>
        /// <param name="component">要注册的组件</param>
        /// <param name="key">与组件关联的键</param>
        /// <param name="persistBetweenScenes">在场景之间保持组件注册</param>
        public static void RegisterComponent(object component, string key, bool persistBetweenScenes = false)
        {
            EnsureSceneAndList();

            lock (s_Entries)
            {
                foreach (ToolRegistryEntry entry in s_Entries)
                {
                    if (entry.@object == component && entry.key == key)
                    {
                        Log.Fatal($"Exists! [{key}]: {component.GetType().Name}");
                        return;
                    }
                }
                
                // Log.Info($"Register[{key}]: {component.GetType().Name}");
                s_Entries.Add(new ToolRegistryEntry { @object = component, key = key, persist = persistBetweenScenes, scene = SceneManager.GetActiveScene() });
            }
        }

        /// <summary>
        /// 从注册表中删除组件
        /// </summary>
        /// <param name="component">要删除的组件</param>
        public static void RemoveComponent(object component)
        {
            EnsureSceneAndList();

            lock (s_Entries)
            {
                foreach (ToolRegistryEntry entry in s_Entries)
                {
                    if (entry.@object == component)
                    {
                        s_Entries.Remove(entry);
                        // Log.Info($"Remove[{entry.key}]: {component.GetType().Name}");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 从注册表中删除指定键的组件
        /// </summary>
        /// <param name="key">与要删除的对象关联的键</param>>
        public static void RemoveComponentsByKey(string key)
        {
            lock (s_Entries)
            {
                List<ToolRegistryEntry> removeList = new List<ToolRegistryEntry>();
                foreach (ToolRegistryEntry entry in s_Entries)
                {
                    if (entry.key == key)
                    {
                        removeList.Add(entry);
                    }
                }

                foreach (ToolRegistryEntry entry in removeList)
                {
                    s_Entries.Remove(entry);
                }
            }
        }

        private static void ClearNonPersist(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;

            UnityEngine.SceneManagement.Scene currentScene = SceneManager.GetActiveScene();
            List<ToolRegistryEntry> persistedEntries = new List<ToolRegistryEntry>();
            foreach (ToolRegistryEntry entry in s_Entries)
            {
                if (entry.persist || entry.scene == currentScene)
                {
                    persistedEntries.Add(entry);
                }
            }

            s_Entries.Clear();
            s_Entries.AddRange(persistedEntries);
        }
        
        private static void EnsureSceneAndList()
        {
            if (!s_Subscribed)
            {
                Clear();
                s_Subscribed = true;
                SceneManager.sceneLoaded += ClearNonPersist;
            }
        }
    }
}
