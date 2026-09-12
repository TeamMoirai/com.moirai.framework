using System;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档对象身份：为场景对象提供跨会话稳定 ID（编辑器期烘焙），无代码保存的 UnityEngine.Object 场景引用字段经本组件持久化。
    /// <para>引用字段捕获 = 存 ID 字符串；恢复 = 经 <see cref="SaveEntityRegistry"/> 反查当前场景内的同名 ID 对象。
    /// 动态生成实体（运行期 <c>Instantiate</c>）的 ID 注入与场景/全局作用域拆分由动态实体持久化提供，本组件面向场景中预置对象。</para>
    /// </summary>
    [AddComponentMenu("Moirai/Save Object Identity")]
    [DisallowMultipleComponent]
    public sealed class SaveObjectIdentity : MonoBehaviour
    {
        /// <summary>稳定 ID（编辑器 OnValidate 空则烘焙 GUID；序列化持久）。</summary>
        [SerializeField] internal string m_Id = string.Empty;

        /// <summary>
        /// 跨会话稳定 ID（运行期新建对象未烘焙时为空串——空 ID 不注册，引用捕获写 Null）。
        /// </summary>
        public string Id => m_Id;

        /// <summary>
        /// 注册到实体注册表。
        /// </summary>
        private void Awake()
        {
            SaveEntityRegistry.Register(this);
        }

        /// <summary>
        /// 从实体注册表注销。
        /// </summary>
        private void OnDestroy()
        {
            SaveEntityRegistry.Unregister(this);
        }

        /// <summary>
        /// 解析组件所在物体的身份组件。
        /// </summary>
        /// <param name="target">目标组件。</param>
        /// <returns>身份组件；目标为 <c>null</c> 或未挂载时返回 <c>null</c>。</returns>
        public static SaveObjectIdentity Resolve(Component target)
        {
            return target == null ? null : target.GetComponent<SaveObjectIdentity>();
        }

        /// <summary>
        /// 解析物体上的身份组件。
        /// </summary>
        /// <param name="target">目标物体。</param>
        /// <returns>身份组件；目标为 <c>null</c> 或未挂载时返回 <c>null</c>。</returns>
        public static SaveObjectIdentity Resolve(GameObject target)
        {
            return target == null ? null : target.GetComponent<SaveObjectIdentity>();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器期烘焙稳定 ID（空则赋新 GUID）。
        /// <para>复制物体（Ctrl+D）会连 ID 一起拷贝——重复 ID 在运行期注册时按首到先得处理并记告警，需重新烘焙时清空 ID 字段即可。</para>
        /// </summary>
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(m_Id))
            {
                m_Id = Guid.NewGuid().ToString("N");
            }
        }
#endif
    }
}
