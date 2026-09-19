using System;
using UnityEngine.LowLevel;
using UnityPlayerLoop = UnityEngine.LowLevel.PlayerLoop;

namespace Moirai.Atropos.FrameLoop
{
    /// <summary>
    /// 将 <see cref="PlayerLoopDriver"/> 的 Drive 回调注入 Unity PlayerLoop。
    /// <para>注入时基于当前 PlayerLoop，保留 UniTask / 第三方已插入的系统。</para>
    /// <para>在 <c>SubsystemRegistration</c> 记录默认循环；Shutdown / 域重载时恢复，避免编辑器状态污染。</para>
    /// <para>ECS/DOTS 若在 BeforeSceneLoad 重置 PlayerLoop，初始化完成后调用 <see cref="Reinject"/>。</para>
    /// </summary>
    public static class PlayerLoopInjector
    {
        /// <summary>Moirai Update 注入点标记类型。</summary>
        public sealed class MoiraiUpdate { }

        /// <summary>Moirai FixedUpdate 注入点标记类型。</summary>
        public sealed class MoiraiFixedUpdate { }

        /// <summary>Moirai LateUpdate 注入点标记类型。</summary>
        public sealed class MoiraiLateUpdate { }

        private static PlayerLoopSystem.UpdateFunction s_UpdateDelegate;
        private static PlayerLoopSystem.UpdateFunction s_FixedUpdateDelegate;
        private static PlayerLoopSystem.UpdateFunction s_LateUpdateDelegate;

        private static bool s_HasDefaultLoop;
        private static PlayerLoopSystem s_DefaultLoop;
        private static bool s_Injected;

        /// <summary>是否已注入 Moirai PlayerLoop 系统。</summary>
        public static bool IsInjected => s_Injected;

        /// <summary>
        /// 在域注册阶段记录默认 PlayerLoop，并缓存 Drive 委托（避免注入时分配）。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureDefaultPlayerLoop()
        {
            s_DefaultLoop = UnityPlayerLoop.GetDefaultPlayerLoop();
            s_HasDefaultLoop = true;
            s_Injected = false;

            s_UpdateDelegate ??= OnUpdateDrive;
            s_FixedUpdateDelegate ??= OnFixedUpdateDrive;
            s_LateUpdateDelegate ??= OnLateUpdateDrive;
        }

        /// <summary>
        /// 确保 Moirai 系统已注入当前 PlayerLoop（幂等）。基于当前循环插入，不覆盖第三方系统。
        /// </summary>
        public static void EnsureInjected()
        {
            if (s_Injected) return;

            s_UpdateDelegate ??= OnUpdateDrive;
            s_FixedUpdateDelegate ??= OnFixedUpdateDrive;
            s_LateUpdateDelegate ??= OnLateUpdateDrive;

            PlayerLoopSystem loop = UnityPlayerLoop.GetCurrentPlayerLoop();

            bool changed = false;
            // 阶段标记类型位于命名空间 UnityEngine.PlayerLoop（非 LowLevel.PlayerLoop 嵌套类型）
            changed |= InsertSystem(
                ref loop,
                typeof(global::UnityEngine.PlayerLoop.Update),
                typeof(MoiraiUpdate),
                s_UpdateDelegate,
                insertAtStart: true);

            changed |= InsertSystem(
                ref loop,
                typeof(global::UnityEngine.PlayerLoop.FixedUpdate),
                typeof(MoiraiFixedUpdate),
                s_FixedUpdateDelegate,
                insertAtStart: true);

            // PreLateUpdate 末尾 ≈ MonoBehaviour.LateUpdate 之后
            changed |= InsertSystem(
                ref loop,
                typeof(global::UnityEngine.PlayerLoop.PreLateUpdate),
                typeof(MoiraiLateUpdate),
                s_LateUpdateDelegate,
                insertAtStart: false);

            if (changed)
            {
                UnityPlayerLoop.SetPlayerLoop(loop);
            }

            // 注入标志以循环实况为准：相位类型缺失（第三方重建循环）时置位会导致永久静默丢驱动
            s_Injected = AllMarkersPresent(loop);
            if (!s_Injected)
            {
                LogUtility.Warning(
                    "PlayerLoopInjector: 注入后校验未在 PlayerLoop 中找到全部 Moirai 标记，" +
                    "循环可能被第三方重建。确认重建完成后再调用 Reinject()。");
            }
        }

        /// <summary>
        /// BeforeSceneLoad 自愈校验：第三方若在 AfterAssembliesLoaded（同阶段晚于本框架）或
        /// BeforeSceneLoad 早期基于默认循环重建 PlayerLoop，会抹掉 Moirai 标记——
        /// 此处按循环实况补插，不信任注入标志位。
        /// <para>未初始化（GameApp 未启动，如 EditMode）时不主动注入；
        /// 若第三方重置发生在更晚时机（如 ECS 自定义 bootstrap 之后），仍需在重置完成后调用 <see cref="Reinject"/>。</para>
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void VerifyInjection()
        {
            if (!s_Injected) return;

            PlayerLoopSystem loop = UnityPlayerLoop.GetCurrentPlayerLoop();
            if (AllMarkersPresent(loop)) return;

            LogUtility.Warning("PlayerLoopInjector: 检测到 PlayerLoop 被第三方重建导致 Moirai 标记丢失，已自动补插。");
            s_Injected = false;
            EnsureInjected();
        }

        /// <summary>
        /// 强制重新注入（ECS/DOTS 或其它库重置 PlayerLoop 后调用）。
        /// </summary>
        public static void Reinject()
        {
            s_Injected = false;
            EnsureInjected();
        }

        /// <summary>
        /// 恢复域注册时记录的默认 PlayerLoop，并标记未注入。
        /// <para>会移除 UniTask 等第三方注入——仅在退出 Play / Shutdown 时调用。</para>
        /// </summary>
        public static void RestoreDefault()
        {
            if (!s_HasDefaultLoop) return;

            UnityPlayerLoop.SetPlayerLoop(s_DefaultLoop);
            s_Injected = false;
        }

        /// <summary>
        /// 仅移除 Moirai 自身系统，保留其它第三方注入（需精细协调时使用）。
        /// </summary>
        public static void RemoveMoiraiSystems()
        {
            PlayerLoopSystem loop = UnityPlayerLoop.GetCurrentPlayerLoop();
            bool changed = RemoveSystem(ref loop, typeof(MoiraiUpdate));
            changed |= RemoveSystem(ref loop, typeof(MoiraiFixedUpdate));
            changed |= RemoveSystem(ref loop, typeof(MoiraiLateUpdate));
            if (changed)
            {
                UnityPlayerLoop.SetPlayerLoop(loop);
            }
            s_Injected = false;
        }

        private static void OnUpdateDrive() => PlayerLoopDriver.DriveUpdate();

        private static void OnFixedUpdateDrive() => PlayerLoopDriver.DriveFixedUpdate();

        private static void OnLateUpdateDrive() => PlayerLoopDriver.DriveLateUpdate();

        private static bool InsertSystem(
            ref PlayerLoopSystem root,
            Type phaseType,
            Type markerType,
            PlayerLoopSystem.UpdateFunction updateFunction,
            bool insertAtStart)
        {
            if (root.type == phaseType)
            {
                if (ContainsMarker(root.subSystemList, markerType)) return false;

                PlayerLoopSystem[] list = root.subSystemList;
                int oldLen = list?.Length ?? 0;
                var newList = new PlayerLoopSystem[oldLen + 1];

                var injected = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = updateFunction
                };

                if (oldLen == 0)
                {
                    newList[0] = injected;
                }
                else if (insertAtStart)
                {
                    newList[0] = injected;
                    Array.Copy(list, 0, newList, 1, oldLen);
                }
                else
                {
                    Array.Copy(list, newList, oldLen);
                    newList[oldLen] = injected;
                }

                root.subSystemList = newList;
                return true;
            }

            PlayerLoopSystem[] children = root.subSystemList;
            if (children == null) return false;

            for (int i = 0; i < children.Length; i++)
            {
                PlayerLoopSystem child = children[i];
                if (InsertSystem(ref child, phaseType, markerType, updateFunction, insertAtStart))
                {
                    children[i] = child;
                    return true;
                }
            }

            return false;
        }

        private static bool RemoveSystem(ref PlayerLoopSystem root, Type markerType)
        {
            PlayerLoopSystem[] children = root.subSystemList;
            if (children == null) return false;

            int index = -1;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == markerType)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                var newList = new PlayerLoopSystem[children.Length - 1];
                if (index > 0) Array.Copy(children, 0, newList, 0, index);
                if (index < children.Length - 1)
                    Array.Copy(children, index + 1, newList, index, children.Length - index - 1);
                root.subSystemList = newList;
                return true;
            }

            for (int i = 0; i < children.Length; i++)
            {
                PlayerLoopSystem child = children[i];
                if (RemoveSystem(ref child, markerType))
                {
                    children[i] = child;
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMarker(PlayerLoopSystem[] list, Type markerType)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].type == markerType) return true;
            }
            return false;
        }

        /// <summary>三个 Moirai 标记是否全部存在于给定循环中（注入完成度实况校验）。</summary>
        private static bool AllMarkersPresent(PlayerLoopSystem loop)
        {
            return HasPhaseMarker(loop, typeof(global::UnityEngine.PlayerLoop.Update), typeof(MoiraiUpdate))
                && HasPhaseMarker(loop, typeof(global::UnityEngine.PlayerLoop.FixedUpdate), typeof(MoiraiFixedUpdate))
                && HasPhaseMarker(loop, typeof(global::UnityEngine.PlayerLoop.PreLateUpdate), typeof(MoiraiLateUpdate));
        }

        private static bool HasPhaseMarker(PlayerLoopSystem root, Type phaseType, Type markerType)
        {
            if (root.type == phaseType) return ContainsMarker(root.subSystemList, markerType);

            PlayerLoopSystem[] children = root.subSystemList;
            if (children == null) return false;

            for (int i = 0; i < children.Length; i++)
            {
                if (HasPhaseMarker(children[i], phaseType, markerType)) return true;
            }

            return false;
        }
    }
}
