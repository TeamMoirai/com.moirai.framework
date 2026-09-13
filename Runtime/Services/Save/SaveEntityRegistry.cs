using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档实体注册表：稳定 ID → 活跃 <see cref="SaveObjectIdentity"/> 的静态查询点（Awake 注册 / OnDestroy 注销）。
    /// <para>无代码保存的场景引用字段经本表反查恢复目标。主线程契约（注册/注销/查询均随 Unity 主线程生命周期与捕获/恢复管线）。</para>
    /// <para>作用域分表（框架作用域铁律）：<b>场景表</b>登记常规场景对象，场景卸载时整体清扫（对象的 OnDestroy 注销已先行，
    /// 清扫兜底残留死引用）；<b>全局表</b>登记 DontDestroyOnLoad 场景对象，跨场景常驻直至注销。注册时按对象所在场景自动路由。</para>
    /// <para>重复 ID 首到先得并记告警（复制物体连 ID 拷贝是已知编辑器工作流陷阱——以首个注册者为准，后者不覆盖，避免引用静默换绑）。</para>
    /// </summary>
    public static class SaveEntityRegistry
    {
        /// <summary>DontDestroyOnLoad 场景名（引擎固定命名，全局作用域路由判据）。</summary>
        private const string DONT_DESTROY_SCENE_NAME = "DontDestroyOnLoad";

        /// <summary>场景作用域表（场景卸载清扫）。</summary>
        private static readonly Dictionary<string, SaveObjectIdentity> s_SceneIdentities = new Dictionary<string, SaveObjectIdentity>(StringComparer.Ordinal);

        /// <summary>全局作用域表（DontDestroyOnLoad 场景对象；跨场景常驻）。</summary>
        private static readonly Dictionary<string, SaveObjectIdentity> s_GlobalIdentities = new Dictionary<string, SaveObjectIdentity>(StringComparer.Ordinal);

        /// <summary>
        /// 静态状态复位（无域重载进入播放时清空双表并重挂场景卸载监听，保证跨会话无脏状态）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_SceneIdentities.Clear();
            s_GlobalIdentities.Clear();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>
        /// 注册身份组件（空 ID 不注册并记告警；重复 ID 首到先得并记告警）。
        /// <para>按对象所在场景路由作用域：DontDestroyOnLoad 场景入全局表，其余入场景表。</para>
        /// </summary>
        /// <param name="identity">身份组件。</param>
        public static void Register(SaveObjectIdentity identity)
        {
            if (identity == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(identity.m_Id))
            {
                LogUtility.Warning("[SaveService] SaveObjectIdentity on '{0}' has empty id (runtime-created object without baked id), skipping registration.", identity.name);
                return;
            }

            Dictionary<string, SaveObjectIdentity> table = ResolveTable(identity);
            if (table.TryGetValue(identity.m_Id, out SaveObjectIdentity existing) && existing != null && !ReferenceEquals(existing, identity))
            {
                LogUtility.Warning("[SaveService] Duplicate SaveObjectIdentity id '{0}' on '{1}' (already held by '{2}') — first registration wins.", identity.m_Id, identity.name, existing.name);
                return;
            }

            table[identity.m_Id] = identity;
        }

        /// <summary>
        /// 注销身份组件（仅当登记在册的仍是同一实例时移除——重复 ID 的首注册者不受后者销毁影响）。
        /// </summary>
        /// <param name="identity">身份组件。</param>
        public static void Unregister(SaveObjectIdentity identity)
        {
            if (identity == null || string.IsNullOrEmpty(identity.m_Id))
            {
                return;
            }

            if (s_SceneIdentities.TryGetValue(identity.m_Id, out SaveObjectIdentity sceneExisting) && ReferenceEquals(sceneExisting, identity))
            {
                s_SceneIdentities.Remove(identity.m_Id);
                return;
            }

            if (s_GlobalIdentities.TryGetValue(identity.m_Id, out SaveObjectIdentity globalExisting) && ReferenceEquals(globalExisting, identity))
            {
                s_GlobalIdentities.Remove(identity.m_Id);
            }
        }

        /// <summary>
        /// 按稳定 ID 反查活跃身份组件（场景表优先，全局表兜底——同 ID 跨作用域撞键时以场景对象为准）。
        /// </summary>
        /// <param name="id">稳定 ID（空/空白直接未命中）。</param>
        /// <param name="identity">命中时的身份组件。</param>
        /// <returns>命中返回 <c>true</c>。</returns>
        public static bool TryFind(string id, out SaveObjectIdentity identity)
        {
            if (string.IsNullOrEmpty(id))
            {
                identity = null;
                return false;
            }

            if (s_SceneIdentities.TryGetValue(id, out identity) && identity != null)
            {
                return true;
            }

            if (s_GlobalIdentities.TryGetValue(id, out identity) && identity != null)
            {
                return true;
            }

            identity = null;
            return false;
        }

        /// <summary>
        /// 判定身份组件所属作用域表（DontDestroyOnLoad 场景 → 全局表）。
        /// </summary>
        /// <param name="identity">身份组件。</param>
        /// <returns>所属作用域表。</returns>
        private static Dictionary<string, SaveObjectIdentity> ResolveTable(SaveObjectIdentity identity)
        {
            UnityEngine.SceneManagement.Scene scene = identity.gameObject.scene;
            return scene.IsValid() && string.Equals(scene.name, DONT_DESTROY_SCENE_NAME, StringComparison.Ordinal)
                ? s_GlobalIdentities
                : s_SceneIdentities;
        }

        /// <summary>
        /// 场景卸载清扫：移除场景表内已随场景销毁的残留登记（全局表常驻不动）。
        /// <para>场景对象的 OnDestroy 注销在本事件前已完成，此处兜底漏网死引用（fake-null）。</para>
        /// </summary>
        /// <param name="scene">已卸载场景。</param>
        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (s_SceneIdentities.Count == 0)
            {
                return;
            }

            List<string> staleKeys = null;
            foreach (KeyValuePair<string, SaveObjectIdentity> pair in s_SceneIdentities)
            {
                // fake-null（已销毁）与仍指向已卸载场景的登记一并清扫
                if (pair.Value == null || pair.Value.gameObject.scene == scene)
                {
                    staleKeys ??= new List<string>();
                    staleKeys.Add(pair.Key);
                }
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                s_SceneIdentities.Remove(staleKeys[i]);
            }
        }

        /// <summary>
        /// 清空双表（测试隔离用；运行期状态复位由 <see cref="ResetStatics"/> 承担）。
        /// </summary>
        internal static void ResetTables()
        {
            s_SceneIdentities.Clear();
            s_GlobalIdentities.Clear();
        }
    }
}
