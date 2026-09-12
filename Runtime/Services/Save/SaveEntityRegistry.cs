using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档实体注册表：稳定 ID → 活跃 <see cref="SaveObjectIdentity"/> 的静态查询点（Awake 注册 / OnDestroy 注销）。
    /// <para>无代码保存的场景引用字段经本表反查恢复目标。主线程契约（注册/注销/查询均随 Unity 主线程生命周期与捕获/恢复管线）。</para>
    /// <para>重复 ID 首到先得并记告警（复制物体连 ID 拷贝是已知编辑器工作流陷阱——以首个注册者为准，后者不覆盖，避免引用静默换绑）。
    /// 场景/全局作用域分表与动态实体生命周期由动态实体持久化阶段扩展。</para>
    /// </summary>
    public static class SaveEntityRegistry
    {
        /// <summary>稳定 ID → 身份组件表（插入序无序约束；查询 O(1)）。</summary>
        private static readonly Dictionary<string, SaveObjectIdentity> s_Identities = new Dictionary<string, SaveObjectIdentity>(StringComparer.Ordinal);

        /// <summary>
        /// 注册身份组件（空 ID 不注册并记告警；重复 ID 首到先得并记告警）。
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

            if (s_Identities.TryGetValue(identity.m_Id, out SaveObjectIdentity existing) && existing != null && !ReferenceEquals(existing, identity))
            {
                LogUtility.Warning("[SaveService] Duplicate SaveObjectIdentity id '{0}' on '{1}' (already held by '{2}') — first registration wins.", identity.m_Id, identity.name, existing.name);
                return;
            }

            s_Identities[identity.m_Id] = identity;
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

            if (s_Identities.TryGetValue(identity.m_Id, out SaveObjectIdentity existing) && ReferenceEquals(existing, identity))
            {
                s_Identities.Remove(identity.m_Id);
            }
        }

        /// <summary>
        /// 按稳定 ID 反查活跃身份组件。
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

            if (s_Identities.TryGetValue(id, out identity) && identity != null)
            {
                return true;
            }

            identity = null;
            return false;
        }
    }
}
