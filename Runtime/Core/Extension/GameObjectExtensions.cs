using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngineInternal;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    public static class GameObjectExtensions
    {
        #region GetComponentNoAlloc

        private static List<Component> m_ComponentCache = new List<Component>();

        /// <summary>
        /// （0GC)获取组件
        /// </summary>
        /// <param name="this"></param>
        /// <param name="componentType"></param>
        /// <returns></returns>
        /// <remarks>不分配无用内存</remarks>
        public static Component GetComponentNoAlloc(this GameObject @this, Type componentType)
        {
            @this.GetComponents(componentType, m_ComponentCache);
            Component component = m_ComponentCache.Count > 0 ? m_ComponentCache[0] : null;
            m_ComponentCache.Clear();
            return component;
        }
        
        /// <summary>
        /// （0GC)获取组件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="this"></param>
        /// <returns></returns>
        /// <remarks>不分配无用内存</remarks>
        public static T GetComponentNoAlloc<T>(this GameObject @this) where T : Component
        {
            @this.GetComponents(typeof(T), m_ComponentCache);
            Component component = m_ComponentCache.Count > 0 ? m_ComponentCache[0] : null;
            m_ComponentCache.Clear();
            return component as T;
        }
        
        #endregion

        /// <summary>
        /// 在特定分支（在层级结构内部）中获取一个“目标”组件。该分支由“分支根对象”定义，而“分支根对象”又由所选的“分支根组件”来确定。返回的组件必须来自“分支根对象”的子对象。
        /// </summary>
        /// <param name="callerComponent">调用者组件</param>
        /// <param name="includeInactive">是否包含未激活的对象？</param>
        /// <typeparam name="T1">分支根组件的类型。</typeparam>
        /// <typeparam name="T2">目标组件的类型。</typeparam>
        /// <returns>目标组件。</returns>
        public static T2 GetComponentInBranch<T1, T2>(this Component callerComponent, bool includeInactive = true) where T1 : Component where T2 : Component
        {
            T1[] rootComponents = callerComponent.transform.root.GetComponentsInChildren<T1>(includeInactive);

            if (rootComponents.Length == 0)
            {
                Debug.LogWarning($"Root component: No objects found with {typeof(T1).Name} component");
                return null;
            }

            for (int i = 0; i < rootComponents.Length; i++)
            {
                T1 rootComponent = rootComponents[i];

                // 调用者是否是此根对象的子对象？
                if (!callerComponent.transform.IsChildOf(rootComponent.transform) && !rootComponent.transform.IsChildOf(callerComponent.transform))
                    continue;

                T2 targetComponent = rootComponent.GetComponentInChildren<T2>(includeInactive);

                if (targetComponent == null)
                    continue;

                return targetComponent;

            }

            return null;
        }

        /// <summary>
        /// 在特定分支（层级结构内）中获取一个“目标”组件。该分支由“分支根对象”界定，而“分支根对象”由选定的“分支根组件”来确定。返回的组件必须来自“分支根对象”的某个子对象。
        /// </summary>
        /// <param name="callerComponent">调用者组件</param>
        /// <param name="includeInactive">是否包含未激活的对象？</param>
        /// <typeparam name="T1">目标组件的类型。</typeparam>
        /// <returns>目标组件。</returns>
        public static T1 GetComponentInBranch<T1>(this Component callerComponent, bool includeInactive = true) where T1 : Component
        {
            return callerComponent.GetComponentInBranch<T1, T1>(includeInactive);
        }

        /// <summary>
        /// 检查一个游戏对象是否是另一个游戏对象的子对象
        /// </summary>
        /// <param name="gameObject">要检查的游戏对象</param>
        /// <param name="parent">要检查的父对象</param>
        /// <returns></returns>
        public static bool IsChildOf(this GameObject gameObject, GameObject parent)
        {
            Transform t = gameObject.transform;
            Transform target = parent.transform;

            while (true)
            {
                if (t.parent == null) return false;
                if (t.parent == target) return true;
                t = t.parent;
            }
        }
        
        /// <summary>
        /// 获取对象、其子级或父级（按此顺序）上的组件，如果未找到，则将该组件添加到该对象中
        /// </summary>
        /// <param name="this"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetComponentAroundOrAdd<T>(this GameObject @this) where T : Component
        {
            T component = @this.GetComponentInChildren<T>(true);
            if (component == null)
            {
                component = @this.GetComponentInParent<T>();
            }
            if (component == null)
            {
                component = @this.AddComponent<T>();
            }
            return component;
        }

        /// <summary>
        /// 获取或增加组件。
        /// </summary>
        /// <typeparam name="T">要获取或增加的组件。</typeparam>
        /// <param name="gameObject">目标对象。</param>
        /// <returns>获取或增加的组件。</returns>
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();

            if (component == null)
            {
                component = gameObject.AddComponent<T>();
            }

            return component;
        }

        /// <summary>
        /// 获取或增加组件。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <param name="type">要获取或增加的组件类型。</param>
        /// <returns>获取或增加的组件。</returns>
        public static Component GetOrAddComponent(this GameObject gameObject, Type type)
        {
            Component component = gameObject.GetComponent(type);

            if (component == null)
            {
                component = gameObject.AddComponent(type);
            }

            return component;
        }

        /// <summary>
        /// 移除组件。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <param name="type">要获取或增加的组件类型。</param>
        /// <exception cref="ArgumentNullException"></exception>
        [TypeInferenceRule(TypeInferenceRules.TypeReferencedByFirstArgument)]
        public static void RemoveMonoBehaviour(this GameObject gameObject, Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            Component component = gameObject.GetComponent(type);

            if (component != null)
            {
                Object.Destroy(component);
            }
        }

        /// <summary>
        /// 移除组件。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <typeparam name="T">要获取或增加的组件类型。</typeparam>
        public static void RemoveMonoBehaviour<T>(this GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();

            if (component != null)
            {
                Object.Destroy(component);
            }
        }

        /// <summary>
        /// 获取 GameObject 是否在场景中。
        /// </summary>
        /// <param name="gameObject">目标对象。</param>
        /// <returns>GameObject 是否在场景中。</returns>
        /// <remarks>若返回 true，表明此 GameObject 是一个场景中的实例对象；若返回 false，表明此 GameObject 是一个 Prefab。</remarks>
        public static bool InScene(this GameObject gameObject)
        {
            return gameObject.scene.name != null;
        }

        private static readonly List<Transform> s_CachedTransforms = new List<Transform>();

        /// <summary>
        /// 递归设置游戏对象的层次。
        /// </summary>
        /// <param name="gameObject"><see cref="GameObject" /> 对象。</param>
        /// <param name="layer">目标层次的编号。</param>
        public static void SetLayerRecursively(this GameObject gameObject, int layer)
        {
            gameObject.GetComponentsInChildren(true, s_CachedTransforms);
            for (int i = 0; i < s_CachedTransforms.Count; i++)
            {
                s_CachedTransforms[i].gameObject.layer = layer;
            }

            s_CachedTransforms.Clear();
        }

        /// <summary>
        /// 激活/停用游戏对象。
        /// </summary>
        /// <param name="go">要处理的游戏对象。</param>
        /// <param name="value">激活或停用对象，<c>true</c> 激活游戏对象，<c>false</c> 停用游戏对象。</param>
        /// <param name="cacheValue">要更新的激活状态缓存</param>
        public static void SetActive(this GameObject go, bool value, ref bool cacheValue)
        {
            if (go != null && value != cacheValue)
            {
                cacheValue = value;
                go.SetActive(value);
            }
        }
        
        /// <summary>
        /// 根据名字找到子节点，主要用于dummy接口。
        /// </summary>
        /// <param name="transform">位置组件。</param>
        /// <param name="name">子节点名字。</param>
        /// <returns>位置组件。</returns>
        public static Transform FindChildByName(this Transform transform, string name)
        {
            if (transform == null)
            {
                return null;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                var childTrans = transform.GetChild(i);
                if (childTrans.name == name)
                {
                    return childTrans;
                }

                var find = FindChildByName(childTrans, name);
                if (find != null)
                {
                    return find;
                }
            }

            return null;
        }
    }
}