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
        /// <para>
        /// 注意：版本号不在本结构内——它存放在 <see cref="TweenTask"/> 的独立
        /// <c>s_Versions</c> 数组中，与状态内容完全解耦，
        /// 因此 <see cref="Reset"/> 可安全使用 <c>this = default</c> 整体覆盖，
        /// 不会破坏 tweenId 的代际唯一性。
        /// </para>
        /// </summary>
        internal struct TweenState
        {
            #region 时间 [TIMING]

            public float ElapsedTime;
            public float Duration;
            public float StartDelay;
            public float DelayTimer;

            public TweenEase Ease;

            #endregion

            #region 循环 [CYCLING]

            public int Cycles;
            public int CurrentCycle;
            public TweenUtility.ECycleMode CycleMode;

            /// <summary>Rewind 模式：当前周期是否为倒放周期（ease 作用于 1-t，等价于原轨迹时间反演）。</summary>
            public bool IsReversed;

            #endregion

            #region 标志 [FLAGS]

            public bool IsActive;

            /// <summary>暂停标志：Update 循环冻结时间推进（含起始延迟倒计时）。由 Pause/Resume API 维护。</summary>
            public bool IsPaused;

            public bool UseUnscaledTime;
            public bool HasDelay;

            /// <summary>目标先于补间销毁时是否记录告警（Delay API 的 warnIfTargetDestroyed）。</summary>
            public bool WarnIfTargetDestroyed;

            #endregion

            #region 目标 [TARGET]

            /// <summary>目标引用（reference type — 不装箱）。</summary>
            public object Target;

            /// <summary>UnityEngine.Object 视图：非 Unity 目标为 null，用于销毁检测。</summary>
            public UnityEngine.Object UnityObject;

            #endregion

            #region 值 [VALUES]

            public float StartX, StartY, StartZ;
            public float EndX, EndY, EndZ;

            /// <summary>额外标量（Color alpha / float tween / Uniform scale）。</summary>
            public float StartExtra;
            public float EndExtra;

            public Color StartColor;
            public Color EndColor;

            /// <summary>路径点（仅 BezierPath 使用，其余为 null）。</summary>
            public Vector3[] PathPoints;

            #endregion

            #region 回调 [CALLBACKS]

            public Action OnComplete;
            public Action<float> OnUpdateFloat;
            public Action<float, float, float> OnUpdateXYZ;

            // 0GC object 路径：直接持有 object 目标，static lambda / 方法组时无闭包分配
            public Action<object, float> OnUpdateObjectFloat;
            public Action<object, Vector3> OnUpdateObjectVector3;

            #endregion

            #region 类型 [TYPE]

            public TweenOperationType OperationType;

            #endregion

            /// <summary>
            /// 重置为默认值（回收时调用）。整体覆盖安全：版本号在外部数组，不受影响。
            /// </summary>
            public void Reset()
            {
                this = default;
            }
        }
    }
}
