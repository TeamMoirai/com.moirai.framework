using System;
using UnityEngine;

namespace Moirai.Atropos
{
    public sealed partial class DefaultTweenHandler
    {
        /// <summary>
        /// Tween 操作类型标记。
        /// </summary>
        internal enum TweenOperationType : byte
        {
            None = 0,

            // Transform — Vector3
            Position,
            LocalPosition,
            RotationVec3,
            LocalRotationVec3,
            ScaleVec3,

            // Transform — 单轴 float
            PositionX,
            PositionY,
            PositionZ,
            LocalPositionX,
            LocalPositionY,
            LocalPositionZ,
            ScaleX,
            ScaleY,
            ScaleZ,

            // Transform — float (Uniform Scale)
            ScaleFloat,

            // Transform — Quaternion
            RotationQuat,
            LocalRotationQuat,

            // SpriteRenderer
            SpriteColor,
            SpriteAlpha,

            // Material
            MaterialColor,

            // UI
            UISliderValue,
            UINormalizedPosition,
            UIHNormalizedPosition,
            UIVNormalizedPosition,
            UIAnchoredPosition,
            UIAnchoredPositionX,
            UIAnchoredPositionY,
            UIAnchoredPosition3D,
            UISizeDelta,
            UIColor,
            UICanvasGroupAlpha,
            UIGraphicAlpha,
            UIFillAmount,

            // Bezier
            MoveBezierPath,

            // Delay
            Delay,

            // Custom
            CustomFloat,
            CustomInt,
            CustomLong,
            CustomVector3,
        }

        /// <summary>
        /// Tween 核心数据结构。值类型，存储在连续数组中，无堆分配。
        /// </summary>
        internal struct TweenState
        {
            // === 版本控制 ===
            /// <summary>版本号，每次回收递增，用于 safe-reference 验证。</summary>
            public int Version;

            // === 时间 ===
            public float ElapsedTime;
            public float Duration;
            public float StartDelay;
            public float StartTime;
            public float DelayTimer;

            public TweenEase Ease;

            // === 循环 ===
            public int Cycles;
            public int CurrentCycle;
            public TweenUtility.ECycleMode CycleMode;

            // === 标志 ===
            public bool IsActive;
            public bool IsPaused;
            public bool UseUnscaledTime;
            public bool HasDelay;

            // === 目标引用（reference type — 不装箱） ===
            public object Target;
            public UnityEngine.Object UnityObject;

            // === 值存储（Vector3 xyz + 通用 float） ===
            public float StartX, StartY, StartZ;
            public float EndX, EndY, EndZ;

            // === 额外标量（Color alpha / float tween / Uniform scale） ===
            public float StartExtra;
            public float EndExtra;

            // === 颜色 ===
            public Color StartColor;
            public Color EndColor;

            // === 路径点（仅 BezierPath 使用，其余为 null） ===
            public Vector3[] PathPoints;

            // === 回调 ===
            public Action OnComplete;
            public Action<float> OnUpdateFloat;
            public Action<float, float, float> OnUpdateXYZ;
            public Action<Color> OnUpdateColor;
            public Action OnUpdateNoValue;

            // === 类型 ===
            public TweenOperationType OperationType;

            /// <summary>
            /// 重置为默认值（回收时调用）。
            /// </summary>
            public void Reset()
            {
                Version++;
                ElapsedTime = 0f;
                Duration = 0f;
                StartDelay = 0f;
                StartTime = 0f;
                DelayTimer = 0f;
                Cycles = 0;
                CurrentCycle = 0;
                CycleMode = TweenUtility.ECycleMode.Restart;
                Ease = TweenUtility.EEase.Linear;
                IsActive = false;
                IsPaused = false;
                UseUnscaledTime = false;
                HasDelay = false;
                Target = null;
                UnityObject = null;
                StartX = StartY = StartZ = 0f;
                EndX = EndY = EndZ = 0f;
                StartExtra = 0f;
                EndExtra = 0f;
                StartColor = default;
                EndColor = default;
                PathPoints = null;
                OnComplete = null;
                OnUpdateFloat = null;
                OnUpdateXYZ = null;
                OnUpdateColor = null;
                OnUpdateNoValue = null;
                OperationType = TweenOperationType.None;
            }
        }
    }
}