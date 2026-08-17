using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    public sealed partial class DefaultTweenHandler
    {
        /// <summary>
        /// Tween 核心更新循环。结构体数组 + 版本号ID，稳态 0 GC。
        /// <para>
        /// 设计要点：
        /// <list type="bullet">
        /// <item>版本号存于独立 <see cref="s_Versions"/> 数组——与状态内容解耦，
        /// Reset 整结构覆盖不会破坏 tweenId 代际唯一性；</item>
        /// <item>回收先于回调（CompleteAt）——回调内对旧 id 的 Stop/Complete 成为 no-op，
        /// 回调内 Create 复用同槽位安全，消除重入"误杀新 tween"缺陷；</item>
        /// <item>用户回调统一 try/catch——单个回调异常不中断整帧更新；</item>
        /// <item>迭代采用 s_Count 快照——回调中新建的 tween 下一帧才开始计时。</item>
        /// </list>
        /// </para>
        /// <para>本类为 DefaultTweenHandler 专用单例状态机：多个 handler 实例共享同一份静态状态。</para>
        /// </summary>
        internal static class TweenTask
        {
            private const int INITIAL_CAPACITY = 256;

            private static TweenState[] s_States;
            private static int[] s_Versions;
            private static bool[] s_InFree;
            private static readonly Stack<int> s_FreeIndices = new(64);
            private static int s_Count;
            private static int s_Capacity;

            // === WaitAsync 挂起注册表（仅在存在等待者时产生开销） ===
            private static readonly object s_AwaiterLock = new();
            private static readonly List<AwaiterEntry> s_Awaiters = new(4);

            static TweenTask()
            {
                s_States = new TweenState[INITIAL_CAPACITY];
                s_Versions = new int[INITIAL_CAPACITY];
                s_InFree = new bool[INITIAL_CAPACITY];
                s_Capacity = INITIAL_CAPACITY;
            }

            #region 静态重置 [STATIC RESET]

            /// <summary>
            /// 关闭域重载（Enter Play Mode Options）时进入 Play 的静态清理：
            /// 清空槽位与代际版本，防陈旧 tweenId 复活；挂起的 awaiter 全部取消。
            /// </summary>
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            internal static void ResetStatics()
            {
                s_Count = 0;
                s_FreeIndices.Clear();
                if (s_States != null) Array.Clear(s_States, 0, s_States.Length);
                if (s_Versions != null) Array.Clear(s_Versions, 0, s_Versions.Length);
                if (s_InFree != null) Array.Clear(s_InFree, 0, s_InFree.Length);

                lock (s_AwaiterLock)
                {
                    for (int i = 0; i < s_Awaiters.Count; i++)
                        s_Awaiters[i].Source.TrySetResult();
                    s_Awaiters.Clear();
                }
            }

            #endregion

            #region ID 编码 [ID ENCODING]

            /// <summary>
            /// 编码 tweenId：高 32 位 = 数组索引，低 32 位 = 版本号。
            /// 版本号从 1 起步（0 为"无 tween"哨兵值）；int 回绕后相邻代际仍可区分。
            /// </summary>
            private static long EncodeId(int index, int version)
            {
                return ((long)index << 32) | (uint)version;
            }

            private static void DecodeId(long tweenId, out int index, out int version)
            {
                index = (int)(tweenId >> 32);
                version = (int)(tweenId & 0xFFFFFFFF);
            }

            private static bool IsValid(int index, int version)
            {
                return index >= 0 && index < s_Count
                    && s_States[index].IsActive
                    && s_Versions[index] == version;
            }

            #endregion

            #region 创建 [CREATE]

            /// <summary>
            /// 创建一个新的 tween，返回编码后的 tweenId。
            /// </summary>
            internal static long Create(in TweenState state)
            {
                Validate(in state);

                int index = FindFreeSlot();
                // 版本号在复用时递增（唯一递增点）：新 tween 的版本与所有历史 ID 不同。
                // 版本存放于独立数组，与槽位内容生命周期完全解耦。
                int version = unchecked(s_Versions[index] + 1);
                s_Versions[index] = version;

                s_States[index] = state;
                s_States[index].IsActive = true;
                s_States[index].DelayTimer = state.HasDelay ? state.StartDelay : 0f;
                return EncodeId(index, version);
            }

            private static void Validate(in TweenState state)
            {
                if (state.Duration < 0f)
                    throw new GameException("Tween duration must be >= 0 (0 = complete on first update).");
                if (state.Cycles < 1)
                    throw new GameException("Tween cycles must be >= 1.");
                if (state.StartDelay < 0f)
                    throw new GameException("Tween startDelay must be >= 0.");
            }

            private static int FindFreeSlot()
            {
                // 优先：空闲索引栈（O(1)，避免大量活跃 tween 时的线性扫描）
                while (s_FreeIndices.Count > 0)
                {
                    int idx = s_FreeIndices.Pop();
                    s_InFree[idx] = false;
                    if (!s_States[idx].IsActive)
                        return idx;
                }

                // 兜底：线性扫描（覆盖从未使用过、尚未入栈的槽位；跳过已在空闲栈中的槽位避免双归属）
                for (int i = 0; i < s_Count; i++)
                {
                    if (!s_States[i].IsActive && !s_InFree[i])
                        return i;
                }

                // 没有空闲槽位，扩容
                if (s_Count >= s_Capacity)
                {
                    s_Capacity *= 2;
                    Array.Resize(ref s_States, s_Capacity);
                    Array.Resize(ref s_Versions, s_Capacity);
                    Array.Resize(ref s_InFree, s_Capacity);
                }

                return s_Count++;
            }

            /// <summary>
            /// 回收槽位：重置状态并压入空闲索引栈，同时结算该槽位的挂起 awaiter。
            /// 不区分结束原因（完成/Stop/销毁/清理）→ awaiter 一律正常返回；
            /// 仅外部 CancellationToken 取消等待路径会以 OCE 结束。
            /// s_InFree 标记防止同一槽位被重复入栈（如 Stop 后回调中再次 Complete 同一 tween）。
            /// </summary>
            private static void Recycle(int index)
            {
                s_States[index].Reset();
                CompleteAwaiters(index);
                if (s_InFree[index]) return;

                s_InFree[index] = true;
                s_FreeIndices.Push(index);
            }

            #endregion

            #region 更新循环 [UPDATE LOOP]

            /// <summary>
            /// 每帧调用（UpdateDriver 注入），驱动所有活跃 tween。
            /// </summary>
            internal static void Update()
            {
                Update(Time.deltaTime, Time.unscaledDeltaTime);
            }

            /// <summary>
            /// 时间步进注入版：EditMode 测试可直接驱动，不依赖引擎时间。
            /// </summary>
            internal static void Update(float deltaTime, float unscaledDeltaTime)
            {
                // 快照迭代：回调中新建的 tween 下一帧才开始计时，杜绝同帧级联
                int count = s_Count;

                for (int i = 0; i < count; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (!state.IsActive) continue;

                    // 1. 目标已销毁 → 中断（kill），不触发 OnComplete：
                    //    与 DOTween/PrimeTween 语义对齐——回调继续持有已销毁目标只会抛 MissingReferenceException。
                    //    注意：ReferenceEquals 判真实引用存在，== 判 Unity 伪 null（已销毁）——
                    //    两个判断不可互换（`uo != null && uo == null` 两边互斥，永不触发）
                    var unityObject = state.UnityObject;
                    if (!ReferenceEquals(unityObject, null) && unityObject == null)
                    {
                        if (state.WarnIfTargetDestroyed)
                            Log.Warning("Tween target destroyed before completion, tween killed. Operation: {0}", state.OperationType);
                        Recycle(i);
                        continue;
                    }

                    // 2. 暂停：冻结时间推进（含延迟倒计时）；销毁检测与回收语义不受暂停影响
                    if (state.IsPaused) continue;

                    float remainingDt = state.UseUnscaledTime ? unscaledDeltaTime : deltaTime;

                    // 3. 起始延迟（耗尽当帧的剩余时间结转给进度，无重复计时）
                    if (state.HasDelay && state.DelayTimer > 0f)
                    {
                        state.DelayTimer -= remainingDt;
                        if (state.DelayTimer > 0f)
                            continue;
                        remainingDt = -state.DelayTimer; // 跨越延迟边界的剩余时间
                        state.DelayTimer = 0f;
                    }

                    // 4. 累积时间
                    state.ElapsedTime += remainingDt;

                    // 5. 归一化时间（Duration<=0 → 视为立即完成：杜绝 NaN 与负时长永生 tween）
                    float normalizedTime = state.Duration > 0f ? state.ElapsedTime / state.Duration : 1f;

                    // 6. 循环推进
                    if (normalizedTime >= 1f)
                    {
                        state.CurrentCycle++;

                        if (state.CurrentCycle >= state.Cycles)
                        {
                            // 所有循环完成
                            CompleteAt(i);
                            continue;
                        }

                        // 保留 overshoot 余量进入下一循环（消除每循环固定的时间损耗）
                        state.ElapsedTime -= state.Duration;

                        switch (state.CycleMode)
                        {
                            case TweenUtility.ECycleMode.Restart:
                                break;

                            case TweenUtility.ECycleMode.Yoyo:
                                // 交换起止值：回程重新施加同一条缓动曲线（镜像轨迹）
                                SwapValues(ref state);
                                break;

                            case TweenUtility.ECycleMode.Incremental:
                                ShiftByDelta(ref state);
                                break;

                            case TweenUtility.ECycleMode.Rewind:
                                // 真时间反转：下一周期沿原轨迹倒放（ease 作用于 1-t）
                                state.IsReversed = !state.IsReversed;
                                break;
                        }

                        normalizedTime = state.Duration > 0f ? state.ElapsedTime / state.Duration : 0f;
                    }

                    // 7. 应用缓动并写入目标
                    ApplyValue(ref state, normalizedTime);
                }
            }

            private static void SwapValues(ref TweenState state)
            {
                (state.StartX, state.EndX) = (state.EndX, state.StartX);
                (state.StartY, state.EndY) = (state.EndY, state.StartY);
                (state.StartZ, state.EndZ) = (state.EndZ, state.StartZ);
                (state.StartExtra, state.EndExtra) = (state.EndExtra, state.StartExtra);
                (state.StartColor, state.EndColor) = (state.EndColor, state.StartColor);
            }

            private static void ShiftByDelta(ref TweenState state)
            {
                // Incremental：先取原始 delta，再整体平移起止值（连续递增）
                float dX = state.EndX - state.StartX;
                float dY = state.EndY - state.StartY;
                float dZ = state.EndZ - state.StartZ;
                float dE = state.EndExtra - state.StartExtra;
                Color dC = state.EndColor - state.StartColor;

                state.StartX = state.EndX;
                state.StartY = state.EndY;
                state.StartZ = state.EndZ;
                state.StartExtra = state.EndExtra;
                state.StartColor = state.EndColor;

                state.EndX += dX;
                state.EndY += dY;
                state.EndZ += dZ;
                state.EndExtra += dE;
                state.EndColor += dC;
            }

            private static void ApplyValue(ref TweenState state, float normalizedTime)
            {
                // Rewind 倒放周期：ease 作用于倒放时间（1-t），等价于原轨迹的时间反演
                float t = state.Ease.Evaluate(state.IsReversed ? 1f - normalizedTime : normalizedTime);

                switch (state.OperationType)
                {
                    // === Transform Vector3 ===
                    case TweenOperationType.Position:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.position = new Vector3(x, y, z);
                        }

                        break;
                    }
                    case TweenOperationType.LocalPosition:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.localPosition = new Vector3(x, y, z);
                        }

                        break;
                    }
                    case TweenOperationType.ScaleVec3:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.localScale = new Vector3(x, y, z);
                        }

                        break;
                    }

                    // === Transform Rotation Vector3 ===
                    case TweenOperationType.RotationVec3:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.eulerAngles = new Vector3(x, y, z);
                        }

                        break;
                    }
                    case TweenOperationType.LocalRotationVec3:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.localEulerAngles = new Vector3(x, y, z);
                        }

                        break;
                    }

                    // === Transform Rotation Quaternion ===
                    case TweenOperationType.RotationQuat:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Quaternion from = new Quaternion(state.StartX, state.StartY, state.StartZ,
                                state.StartExtra);
                            Quaternion to = new Quaternion(state.EndX, state.EndY, state.EndZ, state.EndExtra);
                            trans.rotation = Quaternion.Slerp(from, to, t);
                        }

                        break;
                    }
                    case TweenOperationType.LocalRotationQuat:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Quaternion from = new Quaternion(state.StartX, state.StartY, state.StartZ,
                                state.StartExtra);
                            Quaternion to = new Quaternion(state.EndX, state.EndY, state.EndZ, state.EndExtra);
                            trans.localRotation = Quaternion.Slerp(from, to, t);
                        }

                        break;
                    }

                    // === 单轴 float ===
                    case TweenOperationType.PositionX:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.position;
                            pos.x = state.StartX + t * (state.EndX - state.StartX);
                            trans.position = pos;
                        }

                        break;
                    }
                    case TweenOperationType.PositionY:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.position;
                            pos.y = state.StartY + t * (state.EndY - state.StartY);
                            trans.position = pos;
                        }

                        break;
                    }
                    case TweenOperationType.PositionZ:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.position;
                            pos.z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.position = pos;
                        }

                        break;
                    }
                    case TweenOperationType.LocalPositionX:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.localPosition;
                            pos.x = state.StartX + t * (state.EndX - state.StartX);
                            trans.localPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.LocalPositionY:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.localPosition;
                            pos.y = state.StartY + t * (state.EndY - state.StartY);
                            trans.localPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.LocalPositionZ:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 pos = trans.localPosition;
                            pos.z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.localPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.ScaleX:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 s = trans.localScale;
                            s.x = state.StartX + t * (state.EndX - state.StartX);
                            trans.localScale = s;
                        }

                        break;
                    }
                    case TweenOperationType.ScaleY:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 s = trans.localScale;
                            s.y = state.StartY + t * (state.EndY - state.StartY);
                            trans.localScale = s;
                        }

                        break;
                    }
                    case TweenOperationType.ScaleZ:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            Vector3 s = trans.localScale;
                            s.z = state.StartZ + t * (state.EndZ - state.StartZ);
                            trans.localScale = s;
                        }

                        break;
                    }

                    // === Uniform Scale float ===
                    case TweenOperationType.ScaleFloat:
                    {
                        if (state.Target is Transform trans && trans != null)
                        {
                            float v = state.StartX + t * (state.EndX - state.StartX);
                            trans.localScale = new Vector3(v, v, v);
                        }

                        break;
                    }

                    // === SpriteRenderer ===
                    case TweenOperationType.SpriteColor:
                    {
                        if (state.Target is SpriteRenderer sr && sr != null)
                        {
                            sr.color = UnityEngine.Color.LerpUnclamped(state.StartColor, state.EndColor, t);
                        }

                        break;
                    }
                    case TweenOperationType.SpriteAlpha:
                    {
                        if (state.Target is SpriteRenderer sr && sr != null)
                        {
                            Color c = sr.color;
                            c.a = state.StartExtra + t * (state.EndExtra - state.StartExtra);
                            sr.color = c;
                        }

                        break;
                    }

                    // === Material ===
                    case TweenOperationType.MaterialColor:
                    {
                        if (state.Target is Material mat && mat != null)
                        {
                            mat.color = UnityEngine.Color.LerpUnclamped(state.StartColor, state.EndColor, t);
                        }

                        break;
                    }

                    // === UI ===
                    case TweenOperationType.UISliderValue:
                    {
                        if (state.Target is UnityEngine.UI.Slider slider && slider != null)
                        {
                            slider.value = state.StartX + t * (state.EndX - state.StartX);
                        }

                        break;
                    }
                    case TweenOperationType.UINormalizedPosition:
                    {
                        if (state.Target is UnityEngine.UI.ScrollRect sr && sr != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            sr.normalizedPosition = new Vector2(x, y);
                        }

                        break;
                    }
                    case TweenOperationType.UIHNormalizedPosition:
                    {
                        if (state.Target is UnityEngine.UI.ScrollRect sr && sr != null)
                        {
                            Vector2 pos = sr.normalizedPosition;
                            pos.x = state.StartX + t * (state.EndX - state.StartX);
                            sr.normalizedPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.UIVNormalizedPosition:
                    {
                        if (state.Target is UnityEngine.UI.ScrollRect sr && sr != null)
                        {
                            Vector2 pos = sr.normalizedPosition;
                            pos.y = state.StartY + t * (state.EndY - state.StartY);
                            sr.normalizedPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.UIAnchoredPosition:
                    {
                        if (state.Target is RectTransform rt && rt != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            rt.anchoredPosition = new Vector2(x, y);
                        }

                        break;
                    }
                    case TweenOperationType.UIAnchoredPositionX:
                    {
                        if (state.Target is RectTransform rt && rt != null)
                        {
                            Vector2 pos = rt.anchoredPosition;
                            pos.x = state.StartX + t * (state.EndX - state.StartX);
                            rt.anchoredPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.UIAnchoredPositionY:
                    {
                        if (state.Target is RectTransform rt && rt != null)
                        {
                            Vector2 pos = rt.anchoredPosition;
                            pos.y = state.StartY + t * (state.EndY - state.StartY);
                            rt.anchoredPosition = pos;
                        }

                        break;
                    }
                    case TweenOperationType.UIAnchoredPosition3D:
                    {
                        if (state.Target is RectTransform rt && rt != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            float z = state.StartZ + t * (state.EndZ - state.StartZ);
                            rt.anchoredPosition3D = new Vector3(x, y, z);
                        }

                        break;
                    }
                    case TweenOperationType.UISizeDelta:
                    {
                        if (state.Target is RectTransform rt && rt != null)
                        {
                            float x = state.StartX + t * (state.EndX - state.StartX);
                            float y = state.StartY + t * (state.EndY - state.StartY);
                            rt.sizeDelta = new Vector2(x, y);
                        }

                        break;
                    }
                    case TweenOperationType.UIColor:
                    {
                        if (state.Target is UnityEngine.UI.Graphic g && g != null)
                        {
                            g.color = UnityEngine.Color.LerpUnclamped(state.StartColor, state.EndColor, t);
                        }

                        break;
                    }
                    case TweenOperationType.UICanvasGroupAlpha:
                    {
                        if (state.Target is CanvasGroup cg && cg != null)
                        {
                            cg.alpha = state.StartExtra + t * (state.EndExtra - state.StartExtra);
                        }

                        break;
                    }
                    case TweenOperationType.UIGraphicAlpha:
                    {
                        if (state.Target is UnityEngine.UI.Graphic g && g != null)
                        {
                            Color c = g.color;
                            c.a = state.StartExtra + t * (state.EndExtra - state.StartExtra);
                            g.color = c;
                        }

                        break;
                    }
                    case TweenOperationType.UIFillAmount:
                    {
                        if (state.Target is UnityEngine.UI.Image img && img != null)
                        {
                            img.fillAmount = state.StartExtra + t * (state.EndExtra - state.StartExtra);
                        }

                        break;
                    }

                    // === BezierPath ===
                    case TweenOperationType.MoveBezierPath:
                    {
                        if (state.Target is Transform trans && trans != null && state.PathPoints != null)
                        {
                            trans.position = CalculateBezierPoint(t, state.PathPoints);
                        }

                        break;
                    }

                    // === Delay（仅等待，无实际值操作） ===
                    case TweenOperationType.Delay:
                        break;

                    // === Custom 回调 ===
                    case TweenOperationType.CustomFloat:
                    {
                        float val = state.StartX + t * (state.EndX - state.StartX);
                        var objectCallback = state.OnUpdateObjectFloat;
                        if (objectCallback != null) objectCallback(state.Target, val);
                        else state.OnUpdateFloat?.Invoke(val);
                        break;
                    }
                    case TweenOperationType.CustomInt:
                    {
                        int val = Mathf.RoundToInt(state.StartX + t * (state.EndX - state.StartX));
                        state.OnUpdateFloat?.Invoke(val);
                        break;
                    }
                    case TweenOperationType.CustomLong:
                    {
                        long val = (long)(state.StartX + t * (state.EndX - state.StartX));
                        state.OnUpdateFloat?.Invoke(val);
                        break;
                    }
                    case TweenOperationType.CustomVector3:
                    {
                        float x = state.StartX + t * (state.EndX - state.StartX);
                        float y = state.StartY + t * (state.EndY - state.StartY);
                        float z = state.StartZ + t * (state.EndZ - state.StartZ);
                        var objectCallback = state.OnUpdateObjectVector3;
                        if (objectCallback != null) objectCallback(state.Target, new Vector3(x, y, z));
                        else state.OnUpdateXYZ?.Invoke(x, y, z);
                        break;
                    }
                }
            }

            /// <summary>
            /// 完成路径统一入口：应用终值 → 回收 → 触发 OnComplete。
            /// <para>
            /// 先回收再回调：回调内对旧 id 的 Stop/Complete 因 IsActive 校验成为 no-op；
            /// 回调内 Create 复用本槽位也安全（版本由下一次 Create 递增）——
            /// 消除"回调创建的新 tween 被外层 Recycle 误杀"的重入缺陷。
            /// </para>
            /// </summary>
            private static void CompleteAt(int index)
            {
                ApplyValue(ref s_States[index], 1f);
                Action onComplete = s_States[index].OnComplete;
                Recycle(index);
                InvokeSafe(onComplete);
            }

            /// <summary>
            /// 用户回调统一入口：单个回调异常不中断整帧更新（商业库惯例：捕获并记录）。
            /// </summary>
            private static void InvokeSafe(Action callback)
            {
                if (callback == null) return;

                try
                {
                    callback();
                }
                catch (Exception e)
                {
                    Log.Error("Tween callback threw an exception: {0}", e);
                }
            }

            #endregion

            #region 查询与控制 [QUERY & CONTROL]

            internal static bool IsAlive(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                return IsValid(index, version);
            }

            internal static bool IsTweening(object target)
            {
                for (int i = 0; i < s_Count; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (state.IsActive && ReferenceEquals(state.Target, target))
                        return true;
                }

                return false;
            }

            internal static int GetTweenCount(object target)
            {
                int count = 0;
                for (int i = 0; i < s_Count; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (state.IsActive && ReferenceEquals(state.Target, target))
                        count++;
                }

                return count;
            }

            /// <summary>停止 = 中断：不触发 OnComplete；挂起 awaiter 正常返回（不区分结束原因）。</summary>
            internal static void Stop(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (IsValid(index, version))
                {
                    Recycle(index);
                }
            }

            /// <summary>
            /// 立即完成：应用当前方向的终值并触发 OnComplete（与自然完成同语义）。
            /// Yoyo/Rewind 中途 Complete 落在交换/倒放后的当前终值上。
            /// </summary>
            internal static void Complete(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (IsValid(index, version))
                {
                    CompleteAt(index);
                }
            }

            internal static int StopAll(object target)
            {
                int count = 0;
                for (int i = 0; i < s_Count; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (state.IsActive && (target == null || ReferenceEquals(state.Target, target)))
                    {
                        Recycle(i);
                        count++;
                    }
                }

                return count;
            }

            internal static int CompleteAll(object target)
            {
                int count = 0;
                // 快照：回调中新建的 tween 不在本轮完成范围内（防级联/死循环）
                int total = s_Count;
                for (int i = 0; i < total; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (state.IsActive && (target == null || ReferenceEquals(state.Target, target)))
                    {
                        CompleteAt(i);
                        count++;
                    }
                }

                return count;
            }

            internal static void ReleaseUnused()
            {
                for (int i = 0; i < s_Count; i++)
                {
                    ref TweenState state = ref s_States[i];
                    if (!state.IsActive) continue;

                    // 清理已销毁目标的 tween（常态路径已由 Update 即时处理，此处为兜底）。
                    // ReferenceEquals + Unity 伪 null 组合判断（同 Update 第 1 步）
                    var uo = state.UnityObject;
                    if (!ReferenceEquals(uo, null) && uo == null)
                    {
                        Recycle(i);
                    }
                }

                // 注：尾部压缩已移除——空闲槽位由 s_FreeIndices 管理，
                // 压缩 s_Count 会使栈中 >= s_Count 的索引失效，导致 tween 不可见。
            }

            #endregion

            #region 暂停与等待 [PAUSE & AWAIT]

            /// <summary>暂停指定 tween（冻结时间推进，含延迟倒计时）。死 id 静默 no-op。</summary>
            internal static void Pause(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (IsValid(index, version))
                    s_States[index].IsPaused = true;
            }

            /// <summary>恢复指定 tween。死 id 或未暂停时静默 no-op。</summary>
            internal static void Resume(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (IsValid(index, version))
                    s_States[index].IsPaused = false;
            }

            /// <summary>
            /// 等待 tween 结束（UniTask，即时信号版）。
            /// <para>任何结束原因（自然完成/Complete/Stop/目标销毁/清理）→ 正常返回，不区分死因；
            /// 仅外部 CancellationToken 取消 → OperationCanceledException（放弃等待，tween 不受影响）。</para>
            /// <para>已结束的 id → 立即完成。注册表仅在存在等待者时产生开销。</para>
            /// </summary>
            internal static UniTask WaitAsync(long tweenId, CancellationToken cancellationToken)
            {
                DecodeId(tweenId, out int index, out int version);
                if (!IsValid(index, version))
                    return UniTask.CompletedTask;
                if (cancellationToken.IsCancellationRequested)
                    return UniTask.FromCanceled(cancellationToken);

                var source = AutoResetUniTaskCompletionSource.Create();
                var entry = new AwaiterEntry { Slot = index, Source = source };
                lock (s_AwaiterLock)
                {
                    s_Awaiters.Add(entry);
                    // static lambda + state 对象：委托缓存复用，仅 entry 一次堆分配
                    entry.Registration = cancellationToken.Register(static obj =>
                    {
                        var e = (AwaiterEntry)obj;
                        e.Registration.Dispose();
                        lock (s_AwaiterLock)
                        {
                            if (s_Awaiters.Remove(e))
                                e.Source.TrySetCanceled();
                        }
                    }, entry);
                }

                return source.Task;
            }

            /// <summary>挂起的 awaiter 数量（测试用泄漏检测）。</summary>
            internal static int PendingAwaiterCount
            {
                get
                {
                    lock (s_AwaiterLock) return s_Awaiters.Count;
                }
            }

            /// <summary>
            /// 结算指定槽位的全部挂起 awaiter（Recycle 时调用，统一正常完成——不区分结束原因）。
            /// 池化 source 的版本守卫保证后续的陈旧 TrySet* 全部为 no-op。
            /// </summary>
            private static void CompleteAwaiters(int slot)
            {
                lock (s_AwaiterLock)
                {
                    for (int i = s_Awaiters.Count - 1; i >= 0; i--)
                    {
                        var entry = s_Awaiters[i];
                        if (entry.Slot != slot) continue;

                        s_Awaiters.RemoveAt(i);
                        entry.Registration.Dispose();
                        entry.Source.TrySetResult();
                    }
                }
            }

            private sealed class AwaiterEntry
            {
                public int Slot;
                public AutoResetUniTaskCompletionSource Source;
                public CancellationTokenRegistration Registration;
            }

            #endregion

            #region 贝塞尔 [BEZIER]

            /// <summary>
            /// De Casteljau 算法计算 N 阶贝塞尔曲线。
            /// 无 Pow / 二项式系数计算，复用静态缓冲（按需扩容），0 GC。
            /// </summary>
            private static Vector3[] s_BezierScratch;

            private static Vector3 CalculateBezierPoint(float t, Vector3[] points)
            {
                int n = points.Length - 1;
                if (n < 0) return Vector3.zero;
                if (n == 0) return points[0];

                if (s_BezierScratch == null || s_BezierScratch.Length < points.Length)
                    s_BezierScratch = new Vector3[Mathf.NextPowerOfTwo(points.Length)];

                Array.Copy(points, s_BezierScratch, points.Length);
                float u = 1f - t;
                for (int k = n; k >= 1; k--)
                {
                    for (int i = 0; i < k; i++)
                    {
                        s_BezierScratch[i] = u * s_BezierScratch[i] + t * s_BezierScratch[i + 1];
                    }
                }

                return s_BezierScratch[0];
            }

            #endregion
        }
    }
}
