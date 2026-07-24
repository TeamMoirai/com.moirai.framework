using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 统一缓动参数。通过隐式转换可直接从 <see cref="TweenUtility.EEase"/> 或
    /// <see cref="AnimationCurve"/> 赋值，调用侧无需关心底层差异。
    /// <code>
    /// // 以下三种写法均可：
    /// TweenUtility.Position(t, end, 0.3f); // 默认 Linear
    /// TweenUtility.Position(t, end, 0.3f, TweenUtility.EEase.OutQuad); // 枚举
    /// TweenUtility.Position(t, end, 0.3f, myAnimationCurve); // 曲线
    /// </code>
    /// </summary>
    [Serializable]
    public struct TweenEase : IEquatable<TweenEase>
    {
        /// <summary>缓动数据源类型。</summary>
        public enum ETweenType : byte
        {
            /// <summary>
            /// 内置缓动曲线
            /// </summary>
            Ease,

            /// <summary>
            /// 自定义动画曲线
            /// </summary>
            AnimationCurve
        }

        [JsonSerializeAs("tweenType")]
        [SerializeField] private ETweenType m_TweenType;
        public ETweenType TweenType => m_TweenType;

        [JsonSerializeAs("easeType")]
        [SerializeField] private TweenUtility.EEase m_EaseType;
        public TweenUtility.EEase EaseType => m_EaseType;

        [JsonDoNotSerialize]
        [SerializeField] private AnimationCurve m_AnimationCurve;
        public AnimationCurve AnimationCurve => m_AnimationCurve;
        [JsonSerializeAs("animationKeys")]
        [JsonSerialize] private Keyframe[] _animationKeys; // JSON仅序列化关键帧
        [JsonSerializeAs("preWrapMode")]
        [JsonSerialize] private WrapMode _preWrapMode;
        public WrapMode PreWrap => _preWrapMode;
        [JsonSerializeAs("postWrapMode")]
        [JsonSerialize] private WrapMode _postWrapMode;
        public WrapMode PostWrap => _postWrapMode;
        public int KeyCount => _animationKeys != null ? _animationKeys.Length : 0;

        /// <summary>是否为 AnimationCurve 模式。</summary>
        public bool IsCurve => m_TweenType == ETweenType.AnimationCurve;

        /// <summary>是否为 Ease 枚举模式。</summary>
        public bool IsEase => m_TweenType == ETweenType.Ease;

        #region 构造函数 [CONSTRUCTOR]

        public TweenEase(TweenUtility.EEase ease = TweenUtility.EEase.Linear)
        {
            m_TweenType = ETweenType.Ease;
            m_EaseType = ease;
            m_AnimationCurve = new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(1.0f, 1.0f));
            _animationKeys = null;
            _preWrapMode = default;
            _postWrapMode = default;
        }
        
        public TweenEase(AnimationCurve animationCurve)
        {
            m_TweenType = ETweenType.AnimationCurve;
            m_EaseType = TweenUtility.EEase.Linear;
            m_AnimationCurve = animationCurve;
            _animationKeys = null;
            _preWrapMode = default;
            _postWrapMode = default;
        }
        
        public TweenEase(TweenUtility.EEase ease, AnimationCurve animationCurve, bool easeFirst = true)
        {
            m_TweenType = easeFirst ? ETweenType.Ease : ETweenType.AnimationCurve;
            m_EaseType = ease;
            m_AnimationCurve = animationCurve;
            _animationKeys = null;
            _preWrapMode = default;
            _postWrapMode = default;
        }
        
        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 对归一化时间 t∈[0,1] 求值，返回缓动后的进度。
        /// 允许返回值超出 [0,1]（Back/Elastic 等有超调）。
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public float Evaluate(float t)
        {
            if (m_TweenType == ETweenType.AnimationCurve)
            {
                var curve = m_AnimationCurve;
                if (curve != null) return curve.Evaluate(t);
                // 无有效曲线 → 回退 Linear
            }
            return EaseUtility.Evaluate(m_EaseType, t);
        }

        public override string ToString()
            => m_TweenType == ETweenType.AnimationCurve
                ? $"TweenEase(Curve, {KeyCount} keys)"
                : $"TweenEase({m_EaseType})";

        // Tween type methods ------------------------------------------------------------------------------------------------------------------------

        public float Tween(float currentTime, float initialTime, float endTime, float startValue, float endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public long Tween(float currentTime, float initialTime, float endTime, long startValue, long endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector3 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        #endregion

        #region 隐式转换

        /// <summary>
        /// 从 <see cref="TweenUtility.EEase"/> 隐式构造。
        /// </summary>
        public static implicit operator TweenEase(TweenUtility.EEase ease) => new TweenEase(ease);

        /// <summary>
        /// 从 <see cref="AnimationCurve"/> 隐式构造。
        /// null 回退为 Linear。
        /// </summary>
        public static implicit operator TweenEase(AnimationCurve curve)
            => curve != null ? new TweenEase(curve) : new TweenEase(TweenUtility.EEase.Linear);

        #endregion

        #region 相等性（轻量：不逐帧比较 Keyframe）

        public bool Equals(TweenEase other)
        {
            if (m_TweenType != other.m_TweenType) return false;

            if (m_TweenType == ETweenType.AnimationCurve)
                return _preWrapMode  == other._preWrapMode
                       && _postWrapMode == other._postWrapMode
                       && KeyCount      == other.KeyCount;

            return m_EaseType == other.m_EaseType;
        }

        public override bool Equals(object obj) => obj is TweenEase o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)m_TweenType * 397;
                return m_TweenType == ETweenType.AnimationCurve
                    ? h ^ KeyCount ^ ((int)_preWrapMode * 17) ^ ((int)_postWrapMode * 31)
                    : h ^ (int)m_EaseType;
            }
        }

        public static bool operator ==(TweenEase a, TweenEase b) =>  a.Equals(b);
        public static bool operator !=(TweenEase a, TweenEase b) => !a.Equals(b);

        #endregion

        #region 序列化 [SERIALIZATION]

        [JsonBeforeSerialization]
        public void OnBeforeSerialize()
        {
            if (m_AnimationCurve == null) return;

            _animationKeys = m_AnimationCurve.keys;
            _preWrapMode = m_AnimationCurve.preWrapMode;
            _postWrapMode = m_AnimationCurve.postWrapMode;
        }

        [JsonAfterDeserialization]
        public void OnAfterDeserialize()
        {
            if (m_AnimationCurve == null) m_AnimationCurve = new AnimationCurve();

            m_AnimationCurve.keys = _animationKeys;
            m_AnimationCurve.preWrapMode = _preWrapMode;
            m_AnimationCurve.postWrapMode = _postWrapMode;
        }

        #endregion
    }
}
