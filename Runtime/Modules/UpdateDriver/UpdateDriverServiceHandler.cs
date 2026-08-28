using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Internal;

namespace Moirai.Atropos.UpdateDriver
{
    /// <summary>
    /// 更新驱动处理器。通过常驻 GameObject（MainBehaviour）承载协程与 Unity 帧事件注入。
    /// <para>可在 <see cref="UpdateDriverServiceSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
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
