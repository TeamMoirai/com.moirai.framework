using System;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源服务驱动编排（原独立 <c>ResourceServiceDriver</c> 纯 C# 类并入门面 partial）。
    /// <para>职责：配置单源注入、每帧 Idle/KeepAlive 时间轮推进、无用资源周期卸载调度、
    /// GC.Collect 节流与低内存响应。</para>
    /// </summary>
    public partial class ResourceService
    {
        #region 驱动状态 [DRIVE STATE]

        private static bool s_DriveWired;

        private static bool s_DriveForceUnloadUnusedAssets;
        private static bool s_DriveForceSystemUnloadUnusedAssets;
        private static bool s_DrivePreorderUnloadUnusedAssets;
        private static bool s_DrivePerformGCCollect;

        private static AsyncOperation s_DriveAsyncOperation;
        private static float s_DriveLastUnloadElapsedSeconds;
        private static float s_DriveLastGCCollectElapsedSeconds = float.MaxValue;

        #endregion

        #region 驱动生命周期 [DRIVE LIFECYCLE]

        /// <summary>
        /// 接线资源服务驱动：注入 Settings/UpdateSettings 配置至 Handler，注册帧驱动与低内存回调。
        /// </summary>
        private static void DriveInitialize()
        {
            if (s_Handler == null || s_DriveWired)
            {
                return;
            }

            // 远程地址（更新系统单源）
            s_Handler.HostServerURL = UpdateSettings.GetResDownLoadPath();
            s_Handler.FallbackHostServerURL = UpdateSettings.GetFallbackResDownLoadPath();
            s_Handler.LoadResWayWebGL = (EResourceLoadWayWebGL)UpdateSettings.LoadResWayWebGL;

            // 通用配置（ResourceSettings 单源）
            s_Handler.AssetAutoReleaseInterval = ResourceServiceSettings.AssetAutoReleaseInterval;
            s_Handler.AssetCapacity = ResourceServiceSettings.AssetCapacity;
            s_Handler.AssetExpireTime = ResourceServiceSettings.AssetExpireTime;
            s_Handler.AssetPriority = ResourceServiceSettings.AssetPriority;
            s_Handler.AssetRecordCapacity = ResourceServiceSettings.AssetRecordCapacity;
            s_Handler.AssetLeaseCapacity = ResourceServiceSettings.AssetLeaseCapacity;
            s_Handler.BindingOwnerCapacity = ResourceServiceSettings.BindingOwnerCapacity;
            s_Handler.BindingSlotCapacity = ResourceServiceSettings.BindingSlotCapacity;
            s_Handler.RegisteredTargetCapacity = ResourceServiceSettings.RegisteredTargetCapacity;
            s_Handler.IdleAssetExpireTime = ResourceServiceSettings.IdleAssetExpireTime;
            s_Handler.SetForceUnloadUnusedAssetsAction(RequestForceUnloadUnusedAssets);

            // 初始化后端（创建默认包与绑定服务）
            s_Handler.Initialize();

            Application.lowMemory += DriveOnLowMemory;
            UnityUtility.AddUpdateListener(DriveTick);
            UnityUtility.AddDestroyListener(DriveTeardown);
            s_DriveWired = true;

            LogUtility.Info("ResourceService Run Mode：{0}", s_Handler.PlayMode);
        }

        /// <summary>
        /// 解除驱动接线：注销帧驱动与低内存回调，复位调度状态。
        /// </summary>
        internal static void DriveTeardown()
        {
            if (!s_DriveWired)
            {
                return;
            }

            s_DriveWired = false;
            UnityUtility.RemoveUpdateListener(DriveTick);
            UnityUtility.RemoveDestroyListener(DriveTeardown);
            Application.lowMemory -= DriveOnLowMemory;

            s_DriveAsyncOperation = null;
            s_DriveForceUnloadUnusedAssets = false;
            s_DriveForceSystemUnloadUnusedAssets = false;
            s_DrivePreorderUnloadUnusedAssets = false;
            s_DrivePerformGCCollect = false;
            s_DriveLastUnloadElapsedSeconds = 0f;
            s_DriveLastGCCollectElapsedSeconds = float.MaxValue;
        }

        #endregion

        #region 强制回收入口 [FORCE RECYCLING ENTRY]

        /// <summary>
        /// 请求强制执行释放未被使用的资源（经 SetForceUnloadUnusedAssetsAction 注册到 Handler 的回调入口）。
        /// </summary>
        /// <param name="performGCCollect">是否使用垃圾回收。</param>
        private static void RequestForceUnloadUnusedAssets(bool performGCCollect)
        {
            s_DriveForceUnloadUnusedAssets = true;
            if (performGCCollect)
            {
                s_DrivePerformGCCollect = true;
                s_DriveForceSystemUnloadUnusedAssets = true;
            }
        }

        /// <summary>
        /// 低内存响应转发。
        /// </summary>
        private static void DriveOnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");
            Handler.OnLowMemory();
        }

        #endregion

        #region 帧循环 [FRAME TICK]

        /// <summary>
        /// 每帧驱动：时间轮推进 + 无用资源卸载调度 + GC 节流（与原 ResourceServiceDriver.Update 编排逐行等价）。
        /// </summary>
        private static void DriveTick()
        {
            if (s_Handler == null)
            {
                return;
            }

            float minInterval = ResourceServiceSettings.MinUnloadUnusedAssetsInterval;
            float maxInterval = ResourceServiceSettings.MaxUnloadUnusedAssetsInterval;
            bool useSystem = ResourceServiceSettings.UseSystemUnloadUnusedAssets;
            int expirePerFrame = ResourceServiceSettings.ExpireProcessCountPerFrame;
            int expireWhenUnloading = ResourceServiceSettings.ExpireProcessCountWhenUnloading;
            float minGCInterval = ResourceServiceSettings.MinGCCollectInterval;

            bool operationInFlight = s_DriveAsyncOperation != null;
            bool shouldUnloadUnusedAssets = ShouldUnloadUnusedAssets(
                operationInFlight,
                s_DriveLastUnloadElapsedSeconds,
                s_DriveForceUnloadUnusedAssets,
                s_DrivePreorderUnloadUnusedAssets,
                minInterval,
                maxInterval);

            int expireProcessCount = ResolveExpireProcessCount(shouldUnloadUnusedAssets, expirePerFrame, expireWhenUnloading);
            s_Handler.ProcessKeepAlive(Time.unscaledTime, expireProcessCount);

            s_DriveLastUnloadElapsedSeconds += Time.unscaledDeltaTime;
            s_DriveLastGCCollectElapsedSeconds += Time.unscaledDeltaTime;
            if (shouldUnloadUnusedAssets)
            {
                bool force = s_DriveForceUnloadUnusedAssets;
                bool useSystemUnload = s_DriveForceSystemUnloadUnusedAssets && useSystem;
                s_DriveForceUnloadUnusedAssets = false;
                s_DriveForceSystemUnloadUnusedAssets = false;
                s_DrivePreorderUnloadUnusedAssets = false;
                s_DriveLastUnloadElapsedSeconds = 0f;
                s_Handler.UnloadUnusedAssets(force);
                s_DriveAsyncOperation = useSystemUnload ? Resources.UnloadUnusedAssets() : null;
            }

            if (s_DriveAsyncOperation == null && s_DrivePerformGCCollect)
            {
                TryCollectGarbage(minGCInterval);
            }

            if (s_DriveAsyncOperation is { isDone: true })
            {
                s_DriveAsyncOperation = null;
                if (s_DrivePerformGCCollect)
                {
                    TryCollectGarbage(minGCInterval);
                }
            }
        }

        private static void TryCollectGarbage(float minInterval)
        {
            if (s_DriveLastGCCollectElapsedSeconds < minInterval)
            {
                return;
            }

            LogUtility.Info("GC.Collect...");
            s_DrivePerformGCCollect = false;
            s_DriveLastGCCollectElapsedSeconds = 0f;
            GC.Collect();
        }

        #endregion

        #region 调度决策（纯函数，供回归测试）[SCHEDULING DECISIONS]

        /// <summary>
        /// 判定本帧是否应触发无用资源卸载。
        /// </summary>
        /// <param name="operationInFlight">是否已有卸载操作在途。</param>
        /// <param name="elapsedSinceLastUnload">距上次卸载的经过秒数。</param>
        /// <param name="forceRequested">是否被强制请求。</param>
        /// <param name="preorderRequested">是否被预约请求（低优先级提前卸载）。</param>
        /// <param name="minInterval">预约请求生效所需的最小间隔。</param>
        /// <param name="maxInterval">无请求时的最大间隔。</param>
        /// <returns>是否应触发卸载。</returns>
        internal static bool ShouldUnloadUnusedAssets(bool operationInFlight, float elapsedSinceLastUnload,
            bool forceRequested, bool preorderRequested, float minInterval, float maxInterval)
        {
            return !operationInFlight &&
                   (forceRequested ||
                    elapsedSinceLastUnload >= maxInterval ||
                    preorderRequested && elapsedSinceLastUnload >= minInterval);
        }

        /// <summary>
        /// 计算本帧过期处理预算：常态按每帧配额，进入卸载帧时提升至上限且不低于常态值。
        /// </summary>
        internal static int ResolveExpireProcessCount(bool shouldUnload, int perFrameCount, int whenUnloadingCount)
        {
            return Mathf.Max(shouldUnload ? whenUnloadingCount : 0, perFrameCount);
        }

        #endregion
    }
}
