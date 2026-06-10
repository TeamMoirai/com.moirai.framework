using System;
using System.Collections.Generic;
using Moirai.Atropos.UpdateDriver;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 单例接口。
    /// </summary>
    public interface ISingleton
    {
        /// <summary>
        /// 激活接口，通常用于手动实例化。
        /// </summary>
        public void Active();

        /// <summary>
        /// 释放接口。
        /// </summary>
        public void Release();
    }
    
    public interface IUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnUpdate();
    }

    public interface IFixedUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnFixedUpdate();
    }

    public interface ILateUpdate
    {
        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        void OnLateUpdate();
    }

    public interface IDrawGizmos
    {
        void OnDrawGizmos();
    }
    
    public interface IDrawGizmosSelected
    {
        void OnDrawGizmosSelected();
    }
    
    /// <summary>
    /// 框架中的全局对象与Unity场景依赖相关的DontDestroyOnLoad需要统一管理，方便重启游戏时清除工作
    /// </summary>
    public static class SingletonSystem
    {
        private static IUpdateDriver s_UpdateDriver;
        private static readonly List<ISingleton> s_Singletons = new List<ISingleton>();
        private static readonly List<IUpdate> s_Updates = new List<IUpdate>();
        private static readonly List<IFixedUpdate> s_FixedUpdates = new List<IFixedUpdate>();
        private static readonly List<ILateUpdate> s_LateUpdates = new List<ILateUpdate>();
#if UNITY_EDITOR
        private static readonly List<IDrawGizmos> s_DrawGizmos = new List<IDrawGizmos>();
        private static readonly List<IDrawGizmosSelected> s_DrawGizmosSelecteds = new List<IDrawGizmosSelected>();
#endif

        private static readonly Dictionary<string, GameObject> s_GameObjects = new Dictionary<string, GameObject>();

        public static void Retain(ISingleton singleton)
        {
            CheckInit();

            s_Singletons.Add(singleton);

            BuildLifeCycle(singleton);
        }

        public static void Retain(GameObject go, string key, object singleton)
        {
            CheckInit();

            if (s_GameObjects.TryAdd(key, go))
            {
                BuildLifeCycle(singleton);
            }
        }

        private static void BuildLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                s_Updates.Add(update);
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                s_FixedUpdates.Add(fixedUpdate);
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                s_LateUpdates.Add(lateUpdate);
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                s_DrawGizmos.Add(drawGizmos);
            }

            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                s_DrawGizmosSelecteds.Add(drawGizmosSelected);
            }
#endif
        }

        public static void Release(GameObject go, string key, object singleton)
        {
            if (s_GameObjects != null && s_GameObjects.ContainsKey(key))
            {
                Log.Info($"Release：{key}");
                s_GameObjects.Remove(key);
                Object.Destroy(go);
                ReleaseLifeCycle(singleton);
            }
        }

        public static void Release(ISingleton singleton)
        {
            if (s_Singletons != null && s_Singletons.Contains(singleton))
            {
                s_Singletons.Remove(singleton);
                ReleaseLifeCycle(singleton);
            }
        }

        private static void ReleaseLifeCycle(object singleton)
        {
            Type iUpdate = typeof(IUpdate);
            bool needUpdate = iUpdate.IsInstanceOfType(singleton);
            if (needUpdate && singleton is IUpdate update)
            {
                if (s_Updates.Contains(update))
                {
                    s_Updates.Remove(update);
                }
            }

            Type iFixedUpdate = typeof(IFixedUpdate);
            bool needFixedUpdate = iFixedUpdate.IsInstanceOfType(singleton);
            if (needFixedUpdate && singleton is IFixedUpdate fixedUpdate)
            {
                if (s_FixedUpdates.Contains(fixedUpdate))
                {
                    s_FixedUpdates.Remove(fixedUpdate);
                }
            }

            Type iLateUpdate = typeof(ILateUpdate);
            bool needLateUpdate = iLateUpdate.IsInstanceOfType(singleton);
            if (needLateUpdate && singleton is ILateUpdate lateUpdate)
            {
                if (s_LateUpdates.Contains(lateUpdate))
                {
                    s_LateUpdates.Remove(lateUpdate);
                }
            }

#if UNITY_EDITOR
            Type iDrawGizmos = typeof(IDrawGizmos);
            bool needDrawGizmos = iDrawGizmos.IsInstanceOfType(singleton);
            if (needDrawGizmos && singleton is IDrawGizmos drawGizmos)
            {
                if (s_DrawGizmos.Contains(drawGizmos))
                {
                    s_DrawGizmos.Remove(drawGizmos);
                }
            }

            Type iDrawGizmosSelected = typeof(IDrawGizmosSelected);
            bool needDrawGizmosSelected = iDrawGizmosSelected.IsInstanceOfType(singleton);
            if (needDrawGizmosSelected && singleton is IDrawGizmosSelected drawGizmosSelected)
            {
                if (s_DrawGizmosSelecteds.Contains(drawGizmosSelected))
                {
                    s_DrawGizmosSelecteds.Remove(drawGizmosSelected);
                }
            }
#endif
        }

        public static void Release()
        {
            if (s_GameObjects != null)
            {
                var gameObjectSnapshot = new List<GameObject>(s_GameObjects.Values);
                foreach (var gameObject in gameObjectSnapshot)
                {
                    if (gameObject != null)
                    {
                        Object.Destroy(gameObject);
                    }
                }

                s_GameObjects.Clear();
            }

            if (s_Singletons != null)
            {
                var singletonSnapshot = new List<ISingleton>(s_Singletons);
                for (int i = singletonSnapshot.Count - 1; i >= 0; i--)
                {
                    singletonSnapshot[i]?.Release();
                }

                s_Singletons.Clear();
            }

            s_Updates.Clear();
            s_FixedUpdates.Clear();
            s_LateUpdates.Clear();
#if UNITY_EDITOR
            s_DrawGizmos.Clear();
            s_DrawGizmosSelecteds.Clear();
#endif
            DeInit();

            Resources.UnloadUnusedAssets();
        }

        public static GameObject GetGameObject(string key)
        {
            GameObject go = null;
            if (s_GameObjects != null)
            {
                s_GameObjects.TryGetValue(key, out go);
            }

            return go;
        }

        internal static bool ContainsKey(string key)
        {
            if (s_GameObjects != null)
            {
                return s_GameObjects.ContainsKey(key);
            }

            return false;
        }

        public static void Restart()
        {
            if (Camera.main != null)
            {
                Camera.main.gameObject.SetActive(false);
            }

            Release();
            SceneManager.LoadScene(0);
        }

        internal static ISingleton GetSingleton(string key)
        {
            for (int i = 0; i < s_Singletons.Count; ++i)
            {
                if (s_Singletons[i].ToString() == key)
                {
                    return s_Singletons[i];
                }
            }

            return null;
        }

        #region 生命周期

        private static bool s_IsInit = false;

        private static void CheckInit()
        {
            if (s_IsInit) return;

            s_IsInit = true;

            s_UpdateDriver ??= ModuleSystem.GetModule<IUpdateDriver>();
            s_UpdateDriver.AddUpdateListener(OnUpdate);
            s_UpdateDriver.AddFixedUpdateListener(OnFixedUpdate);
            s_UpdateDriver.AddLateUpdateListener(OnLateUpdate);
#if UNITY_EDITOR
            s_UpdateDriver.AddOnDrawGizmosListener(OnDrawGizmos);
            s_UpdateDriver.AddOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
#endif
        }

        private static void DeInit()
        {
            if (!s_IsInit) return;

            s_IsInit = false;

            s_UpdateDriver ??= ModuleSystem.GetModule<IUpdateDriver>();
            s_UpdateDriver.RemoveUpdateListener(OnUpdate);
            s_UpdateDriver.RemoveFixedUpdateListener(OnFixedUpdate);
            s_UpdateDriver.RemoveLateUpdateListener(OnLateUpdate);
#if UNITY_EDITOR
            s_UpdateDriver.RemoveOnDrawGizmosListener(OnDrawGizmos);
            s_UpdateDriver.RemoveOnDrawGizmosSelectedListener(OnDrawGizmosSelected);
#endif
        }

        private static void OnUpdate()
        {
            foreach (var update in s_Updates)
            {
                update.OnUpdate();
            }
        }

        private static void OnFixedUpdate()
        {
            foreach (var fixedUpdate in s_FixedUpdates)
            {
                fixedUpdate.OnFixedUpdate();
            }
        }

        private static void OnLateUpdate()
        {
            foreach (var lateUpdate in s_LateUpdates)
            {
                lateUpdate.OnLateUpdate();
            }
        }

        private static void OnDrawGizmos()
        {
#if UNITY_EDITOR
            foreach (var drawGizmo in s_DrawGizmos)
            {
                drawGizmo.OnDrawGizmos();
            }
#endif
        }

        private static void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            foreach (var drawGizmosSelected in s_DrawGizmosSelecteds)
            {
                drawGizmosSelected.OnDrawGizmosSelected();
            }
#endif
        }
        #endregion
    }
}