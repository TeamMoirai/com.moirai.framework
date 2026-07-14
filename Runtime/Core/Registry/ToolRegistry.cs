using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 存储和检索已注册组件的静态注册表。
    /// </summary>
    /// <remarks>使用此系统可提供比 FindObject 方法更快的结果，并且非常适合需要由多个游戏对象引用的非单例组件。</remarks>
    public static class ToolRegistry
    {
        private struct RegistryEntry
        {

            #region 变量 [VARIABLES]

            public string Key { get; }

            public object Object { get; }

            public bool Persist { get; }

            public UnityEngine.SceneManagement.Scene Scene { get; }

            #endregion

            public RegistryEntry(string key, object obj, bool persist, UnityEngine.SceneManagement.Scene scene)
            {
                Key = key;
                Object = obj;
                Persist = persist;
                Scene = scene;
            }

        }

        private static readonly List<RegistryEntry> s_Entries = new List<RegistryEntry>();
        private static bool s_Subscribed;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        public static void InitializeOnEnterPlayMode()
        {
            s_Entries.Clear();
        }
#endif

        static ToolRegistry()
        {
            if (Application.isPlaying)
            {
                SceneManager.sceneLoaded += ClearNonPersist;
            }

            Clear();
        }

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
            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object != null && s_Entries[i].Object is T t)
                    {
                        return t;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 获取已注册的组件
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static object GetComponent(Type t)
        {
            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object != null && t.IsInstanceOfType(s_Entries[i].Object))
                    {
                        return s_Entries[i].Object;
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// 使用键获取已注册的组件
        /// </summary>
        /// <param name="key">要查找的键</param>
        /// <param name="entityFallback"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetComponent<T>(string key, bool entityFallback = false)
        {
            if (string.IsNullOrEmpty(key)) return GetComponent<T>();

            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Key == key && s_Entries[i].Object is T t)
                    {
                        return t;
                    }
                }

                if (!entityFallback) return default;

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
        /// 获取已注册组件的列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> GetComponents<T>()
        {
            List<T> results = new List<T>();
            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object is T t)
                    {
                        results.Add(t);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 获取已注册组件的列表（0GC）
        /// </summary>
        /// <param name="results"></param>
        /// <typeparam name="T"></typeparam>
        public static void GetComponents<T>(List<T> results)
        {
            results.Clear();
            lock (s_Entries)
            {
                for (int i = 0; i < s_Entries.Count; i++)
                {
                    if (s_Entries[i].Object is T t)
                        results.Add(t);
                }
            }
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
            if (component == null) return;

            lock (s_Entries)
            {
                foreach (RegistryEntry entry in s_Entries)
                {
                    if (entry.Object == component && entry.Key == key)
                    {
                        Log.Fatal($"Exists! [{key}]: {component.GetType().Name}");
                        return;
                    }
                }

                // Log.Info($"Register[{key}]: {component.GetType().Name}");
                s_Entries.Add(new RegistryEntry(key, component, persistBetweenScenes, SceneManager.GetActiveScene()));
            }
        }

        /// <summary>
        /// 从注册表中删除组件
        /// </summary>
        /// <param name="component">要删除的组件</param>
        public static void RemoveComponent(object component)
        {
            lock (s_Entries)
            {
                s_Entries.RemoveAll(entry => entry.Object == component);
            }
        }

        public static void RemoveComponent(object component, string key)
        {
            lock (s_Entries)
            {
                s_Entries.RemoveAll(entry => entry.Object == component && entry.Key == key);
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
                s_Entries.RemoveAll(entry => entry.Key == key);
            }
        }

        private static void ClearNonPersist(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;

            UnityEngine.SceneManagement.Scene currentScene = SceneManager.GetActiveScene();
            List<RegistryEntry> persistedEntries = new List<RegistryEntry>();
            foreach (RegistryEntry entry in s_Entries)
            {
                if (entry.Persist || entry.Scene == currentScene)
                {
                    persistedEntries.Add(entry);
                }
            }

            s_Entries.Clear();
            s_Entries.AddRange(persistedEntries);
        }

    }
}