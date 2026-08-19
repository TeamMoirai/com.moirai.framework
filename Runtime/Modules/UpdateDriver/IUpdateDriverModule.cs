using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Internal;

namespace Moirai.Atropos.UpdateDriver
{
    /// <summary>
    /// 更新驱动模块接口。提供协程控制与 Unity 生命周期事件注入能力。
    /// </summary>
    public interface IUpdateDriverModule
    {
        #region 控制协程 [COROUTINE CONTROL]

        /// <summary>
        /// 启动协程（按方法名）。
        /// </summary>
        /// <param name="methodName">协程方法名。</param>
        /// <returns>协程实例。</returns>
        public Coroutine StartCoroutine(string methodName);

        /// <summary>
        /// 启动协程（按迭代器）。
        /// </summary>
        /// <param name="routine">协程迭代器。</param>
        /// <returns>协程实例。</returns>
        public Coroutine StartCoroutine(IEnumerator routine);

        /// <summary>
        /// 启动协程（按方法名+参数）。
        /// </summary>
        /// <param name="methodName">协程方法名。</param>
        /// <param name="value">传递给协程的参数。</param>
        /// <returns>协程实例。</returns>
        public Coroutine StartCoroutine(string methodName, [DefaultValue("null")] object value);

        /// <summary>
        /// 停止协程（按方法名）。
        /// </summary>
        /// <param name="methodName">协程方法名。</param>
        public void StopCoroutine(string methodName);

        /// <summary>
        /// 停止协程（按迭代器）。
        /// </summary>
        /// <param name="routine">协程迭代器。</param>
        public void StopCoroutine(IEnumerator routine);

        /// <summary>
        /// 停止协程（按协程实例）。
        /// </summary>
        /// <param name="routine">协程实例。</param>
        public void StopCoroutine(Coroutine routine);

        /// <summary>
        /// 停止所有协程。
        /// </summary>
        public void StopAllCoroutines();

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 为给外部提供的 添加帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddFixedUpdateListener(Action action);

        /// <summary>
        /// 为给外部提供的 添加Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddLateUpdateListener(Action action);

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveUpdateListener(Action action);

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveFixedUpdateListener(Action action);

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveLateUpdateListener(Action action);

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 为给外部提供的Destroy注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的Destroy反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveDestroyListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmos反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnDrawGizmosSelected反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnDrawGizmosSelectedListener(Action action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void AddOnApplicationPauseListener(Action<bool> action);

        /// <summary>
        /// 为给外部提供的OnApplicationPause反注册事件。
        /// </summary>
        /// <param name="action"></param>
        public void RemoveOnApplicationPauseListener(Action<bool> action);

        #endregion
    }
}