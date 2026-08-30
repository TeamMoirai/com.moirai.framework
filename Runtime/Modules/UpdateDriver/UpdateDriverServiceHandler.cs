using System;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Internal;

namespace Moirai.Atropos.UpdateDriver
{
    /// <summary>
    /// 更新驱动服务配置抽象基类（纯数据，无行为无生命周期）。
    /// <para>以 <see cref="UnityEngine.SerializeReference"/> 存于 <see cref="UpdateDriverServiceSettings"/> 资产；
    /// 经 <see cref="CreateHandler"/> 工厂创建绑定的后端处理器实例，处理器不再被序列化。</para>
    /// </summary>
    [Serializable]
    public abstract class UpdateDriverServiceConfig
    {
        /// <summary>
        /// 创建配置绑定的更新驱动后端处理器实例。
        /// </summary>
        /// <returns>新的更新驱动处理器实例。</returns>
        public abstract UpdateDriverServiceHandler CreateHandler();
    }

    /// <summary>
    /// 更新驱动处理器。通过常驻 GameObject（MainBehaviour）承载协程与 Unity 帧事件注入。
    /// <para>配置数据由 <see cref="UpdateDriverServiceConfig"/> 系列纯数据类承载——处理器实例本身不再被序列化，
    /// 由 <see cref="UpdateDriverServiceConfig.CreateHandler"/> 工厂在运行期创建。</para>
    /// </summary>
    public abstract class UpdateDriverServiceHandler : FrameworkHandler
    {
        #region 控制协程 [COROUTINE CONTROL]

        public abstract Coroutine StartCoroutine(string methodName);

        public abstract Coroutine StartCoroutine(IEnumerator routine);

        public abstract Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value);

        public abstract void StopCoroutine(string methodName);

        public abstract void StopCoroutine(IEnumerator routine);

        public abstract void StopCoroutine(Coroutine routine);

        public abstract void StopAllCoroutines();

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 为给外部提供的 添加帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddFixedUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddLateUpdateListener(Action action);

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveUpdateListener(Action action);

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveFixedUpdateListener(Action action);

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveLateUpdateListener(Action action);

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 为给外部提供的Destroy注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的Destroy反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void AddOnApplicationPauseListener(Action<bool> action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public abstract void RemoveOnApplicationPauseListener(Action<bool> action);

        #endregion
    }
}
