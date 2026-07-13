using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    public sealed partial class DefaultTweenHandler
    {
        /// <summary>
        /// Tween 核心更新循环。结构体数组 + 版本号ID，0 GC。
        /// </summary>
        internal static class TweenTask
        {
            private const int InitialCapacity = 256;

            private static TweenState[] _states;
            private static int _count;
            private static int _capacity;
            private static readonly List<int> _pendingRemove = new(64);
            private static readonly List<int> _pendingComplete = new(64);

            static TweenTask()
            {
                _states = new TweenState[InitialCapacity];
                _capacity = InitialCapacity;
                _count = 0;
            }

            #region ID 编码

            /// <summary>
            /// 编码 tweenId：高 32 位 = 数组索引，低 32 位 = 版本号。
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

            #endregion

            #region 创建

            /// <summary>
            /// 创建一个新的 tween，返回编码后的 tweenId。
            /// </summary>
            internal static long Create(in TweenState state)
            {
                int index = FindFreeSlot();
                _states[index] = state;
                _states[index].IsActive = true;
                _states[index].StartTime = -1f; // 标记为未开始（等待 StartDelay）
                _states[index].DelayTimer = state.HasDelay ? state.StartDelay : 0f;
                return EncodeId(index, _states[index].Version);
            }

            private static int FindFreeSlot()
            {
                // 从尾部向前找已回收的槽位
                for (int i = 0; i < _count; i++)
                {
                    if (!_states[i].IsActive)
                        return i;
                }

                // 没有空闲槽位，扩容
                if (_count >= _capacity)
                {
                    _capacity *= 2;
                    Array.Resize(ref _states, _capacity);
                }

                return _count++;
            }

            #endregion

            #region 更新循环

            /// <summary>
            /// 每帧调用，驱动所有活跃 tween。
            /// </summary>
            internal static void Update()
            {
                float dt = Time.deltaTime;
                float unscaledDt = Time.unscaledDeltaTime;

                _pendingRemove.Clear();
                _pendingComplete.Clear();

                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (!state.IsActive) continue;

                    // 1. 检查目标是否已销毁
                    if (state.UnityObject != null && state.UnityObject == null)
                    {
                        OnTweenCompleted(ref state, i);
                        continue;
                    }

                    float frameDt = state.UseUnscaledTime ? unscaledDt : dt;

                    // 2. 处理延迟
                    if (state.HasDelay && state.DelayTimer > 0f)
                    {
                        state.DelayTimer -= frameDt;
                        if (state.DelayTimer > 0f)
                            continue;
                        state.DelayTimer = 0f;
                    }

                    // 3. 累积时间
                    state.ElapsedTime += frameDt;

                    // 4. 计算归一化时间
                    float normalizedTime = state.ElapsedTime / state.Duration;

                    // 5. 处理循环
                    if (normalizedTime >= 1f)
                    {
                        state.CurrentCycle++;

                        if (state.CurrentCycle >= state.Cycles)
                        {
                            // 所有循环完成
                            normalizedTime = 1f;
                            ApplyValue(ref state, normalizedTime);
                            OnTweenCompleted(ref state, i);
                            continue;
                        }

                        // 进入下一个循环
                        switch (state.CycleMode)
                        {
                            case TweenUtility.ECycleMode.Restart:
                                state.ElapsedTime = 0f;
                                normalizedTime = 0f;
                                break;

                            case TweenUtility.ECycleMode.Yoyo:
                                state.ElapsedTime = 0f;
                                normalizedTime = 0f;
                                // 交换起止值
                                SwapValues(ref state);
                                break;

                            case TweenUtility.ECycleMode.Incremental:
                                state.StartX = state.EndX;
                                state.StartY = state.EndY;
                                state.StartZ = state.EndZ;
                                state.StartExtra = state.EndExtra;
                                state.StartColor = state.EndColor;
                                state.ElapsedTime = 0f;
                                normalizedTime = 0f;
                                break;

                            case TweenUtility.ECycleMode.Rewind:
                                state.ElapsedTime = 0f;
                                normalizedTime = 0f;
                                // 缓动曲线自动反转（通过 swap start/end 实现视觉反转）
                                SwapValues(ref state);
                                break;
                        }
                    }

                    // 6. 应用缓动并写入目标
                    ApplyValue(ref state, normalizedTime);
                }

                // 7. 批量清理已完成的条目（倒序删除避免索引偏移）
                for (int i = _pendingRemove.Count - 1; i >= 0; i--)
                {
                    int idx = _pendingRemove[i];
                    _states[idx].Reset();
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

            private static void ApplyValue(ref TweenState state, float normalizedTime)
            {
                EEaseType easeType = MapEase(state.Ease);
                float t = EaseUtility.Evaluate(normalizedTime, easeType);

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
                        state.OnUpdateFloat?.Invoke(val);
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
                        state.OnUpdateXYZ?.Invoke(x, y, z);
                        break;
                    }
                }
            }

            private static void OnTweenCompleted(ref TweenState state, int index)
            {
                state.OnComplete?.Invoke();
                _pendingRemove.Add(index);
            }

            #endregion

            #region 查询与控制

            internal static bool IsAlive(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                return index >= 0 && index < _count
                                  && _states[index].IsActive
                                  && _states[index].Version == version;
            }

            internal static bool IsTweening(object target)
            {
                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (state.IsActive && ReferenceEquals(state.Target, target))
                        return true;
                }

                return false;
            }

            internal static int GetTweenCount(object target)
            {
                int count = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (state.IsActive && ReferenceEquals(state.Target, target))
                        count++;
                }

                return count;
            }

            internal static void Stop(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (index >= 0 && index < _count
                               && _states[index].IsActive
                               && _states[index].Version == version)
                {
                    _states[index].IsActive = false;
                    _pendingRemove.Add(index);
                }
            }

            internal static void Complete(long tweenId)
            {
                DecodeId(tweenId, out int index, out int version);
                if (index >= 0 && index < _count
                               && _states[index].IsActive
                               && _states[index].Version == version)
                {
                    ApplyValue(ref _states[index], 1f);
                    _states[index].IsActive = false;
                    _pendingRemove.Add(index);
                }
            }

            internal static int StopAll(object target)
            {
                int count = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (state.IsActive && (target == null || ReferenceEquals(state.Target, target)))
                    {
                        state.IsActive = false;
                        _pendingRemove.Add(i);
                        count++;
                    }
                }

                return count;
            }

            internal static int CompleteAll(object target)
            {
                int count = 0;
                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (state.IsActive && (target == null || ReferenceEquals(state.Target, target)))
                    {
                        ApplyValue(ref state, 1f);
                        state.IsActive = false;
                        _pendingRemove.Add(i);
                        count++;
                    }
                }

                return count;
            }

            internal static void ReleaseUnused()
            {
                for (int i = 0; i < _count; i++)
                {
                    ref TweenState state = ref _states[i];
                    if (!state.IsActive) continue;

                    // 清理已销毁目标的 tween
                    if (state.UnityObject != null && state.UnityObject == null)
                    {
                        state.IsActive = false;
                        state.Reset();
                    }
                }

                // 压缩数组（移除尾部空闲槽位）
                while (_count > 0 && !_states[_count - 1].IsActive)
                    _count--;
            }

            #endregion

            #region 缓动映射

            internal static EEaseType MapEase(TweenUtility.EEase ease)
            {
                return ease switch
                {
                    TweenUtility.EEase.Default => EEaseType.InCubic,
                    TweenUtility.EEase.Linear => EEaseType.Linear,
                    TweenUtility.EEase.InSine => EEaseType.InSine,
                    TweenUtility.EEase.OutSine => EEaseType.OutSine,
                    TweenUtility.EEase.InOutSine => EEaseType.InOutSine,
                    TweenUtility.EEase.InQuad => EEaseType.InQuad,
                    TweenUtility.EEase.OutQuad => EEaseType.OutQuad,
                    TweenUtility.EEase.InOutQuad => EEaseType.InOutQuad,
                    TweenUtility.EEase.InCubic => EEaseType.InCubic,
                    TweenUtility.EEase.OutCubic => EEaseType.OutCubic,
                    TweenUtility.EEase.InOutCubic => EEaseType.InOutCubic,
                    TweenUtility.EEase.InQuart => EEaseType.InQuart,
                    TweenUtility.EEase.OutQuart => EEaseType.OutQuart,
                    TweenUtility.EEase.InOutQuart => EEaseType.InOutQuart,
                    TweenUtility.EEase.InQuint => EEaseType.InQuint,
                    TweenUtility.EEase.OutQuint => EEaseType.OutQuint,
                    TweenUtility.EEase.InOutQuint => EEaseType.InOutQuint,
                    TweenUtility.EEase.InExpo => EEaseType.InExpo,
                    TweenUtility.EEase.OutExpo => EEaseType.OutExpo,
                    TweenUtility.EEase.InOutExpo => EEaseType.InOutExpo,
                    TweenUtility.EEase.InCirc => EEaseType.InCirc,
                    TweenUtility.EEase.OutCirc => EEaseType.OutCirc,
                    TweenUtility.EEase.InOutCirc => EEaseType.InOutCirc,
                    TweenUtility.EEase.InElastic => EEaseType.InElastic,
                    TweenUtility.EEase.OutElastic => EEaseType.OutElastic,
                    TweenUtility.EEase.InOutElastic => EEaseType.InOutElastic,
                    TweenUtility.EEase.InBack => EEaseType.InBack,
                    TweenUtility.EEase.OutBack => EEaseType.OutBack,
                    TweenUtility.EEase.InOutBack => EEaseType.InOutBack,
                    TweenUtility.EEase.InBounce => EEaseType.InBounce,
                    TweenUtility.EEase.OutBounce => EEaseType.OutBounce,
                    TweenUtility.EEase.InOutBounce => EEaseType.InOutBounce,
                    _ => EEaseType.InCubic,
                };
            }

            #endregion

            #region Bezier

            /// <summary>
            /// N 阶贝塞尔曲线计算（复用 TweenHandler.CalculateBezierPoint 逻辑）。
            /// </summary>
            private static Vector3 CalculateBezierPoint(float t, Vector3[] points)
            {
                int n = points.Length - 1;
                Vector3 point = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float coefficient = BinomialCoefficient(n, i) * Mathf.Pow(1 - t, n - i) * Mathf.Pow(t, i);
                    point += coefficient * points[i];
                }

                return point;
            }

            private static int BinomialCoefficient(int n, int k)
            {
                if (k < 0 || k > n) return 0;
                if (k == 0 || k == n) return 1;
                int result = 1;
                for (int i = 0; i < k; i++)
                {
                    result *= (n - i);
                    result /= (i + 1);
                }

                return result;
            }

            #endregion
        }
    }
}