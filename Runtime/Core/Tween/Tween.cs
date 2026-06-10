using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 补间（缓动）的类型
    /// </summary>
    [Serializable]
    public class Tween
    {
        [SerializeField] private TweenTypes m_TweenType;
        [SerializeField] private EEaseType m_EaseType;
        [SerializeField, JsonDoNotSerialize] private AnimationCurve m_AnimationCurve;
        [JsonSerializeAs("animationKeys")]
        [JsonSerialize] private Keyframe[] _animationKeys; // JSON仅序列化关键帧

        public TweenTypes TweenType => m_TweenType;
        public EEaseType EaseType => m_EaseType;
        public AnimationCurve AnimationCurve => m_AnimationCurve;
        
        #region 构造函数 [CONSTRUCTOR]

        public Tween()
        {
            m_TweenType = TweenTypes.Tween;
            m_EaseType = EEaseType.InCubic;
            m_AnimationCurve = new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(1.0f, 1.0f));
        }
        
        public Tween(EEaseType ease)
        {
            m_TweenType = TweenTypes.Tween;
            m_EaseType = ease;
            m_AnimationCurve = new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(1.0f, 1.0f));
        }
        
        public Tween(AnimationCurve animationCurve)
        {
            m_TweenType = TweenTypes.AnimationCurve;
            m_EaseType = EEaseType.Linear;
            m_AnimationCurve = animationCurve;
        }
        
        public Tween(EEaseType ease, AnimationCurve animationCurve, bool easeFirst = true)
        {
            m_TweenType = easeFirst ? TweenTypes.Tween : TweenTypes.AnimationCurve;
            m_EaseType = ease;
            m_AnimationCurve = animationCurve;
        }
        
        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 计算补间值
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public float Evaluate(float t)
        {
            return m_TweenType switch
            {
                TweenTypes.Tween => EaseUtility.Evaluate(t, m_EaseType),
                TweenTypes.AnimationCurve => m_AnimationCurve.Evaluate(t),
                _ => 0f
            };
        }

        // Tween type methods ------------------------------------------------------------------------------------------------------------------------

        public float Float(float currentTime, float initialTime, float endTime, float startValue, float endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public long Long(float currentTime, float initialTime, float endTime, long startValue, long endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector2 Vector2(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector3 Vector3(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Vector3 Vector4(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        public Quaternion Quaternion(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue)
            => EaseUtility.Tween(currentTime, initialTime, endTime, startValue, endValue, this);

        #endregion

        #region 序列化 [SERIALIZATION]

        [JsonBeforeSerialization]
        public void OnBeforeSerialize()
        {
            _animationKeys = m_AnimationCurve.keys;
        }

        [JsonAfterDeserialization]
        public void OnAfterDeserialize()
        {
            m_AnimationCurve.keys = _animationKeys;
        }

        #endregion
    }
}
