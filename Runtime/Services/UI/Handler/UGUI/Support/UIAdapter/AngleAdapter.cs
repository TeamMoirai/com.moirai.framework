using UnityEngine;

namespace Moirai.Atropos.UI.Adapter
{
    public class AngleAdapter : AdapterBase
    {
        [Header("间隙")]
        [SerializeField] internal float m_Gap = 0;
        [Header("是否每帧都计算")]
        [SerializeField] private bool m_CalculateEveryFrame = true;
        [Header("顺时针")]
        [SerializeField] internal bool m_Clockwise = true;
        [Header("偏移")]
        [SerializeField] private float m_BiasAngle = 0;
        [Header("圆心距离")]
        [SerializeField] internal float m_Distance;
        
        private RectTransform _selfRect;
        private RectTransform SelfRect
        {
            get
            {
                if (_selfRect == null)
                {
                    _selfRect = GetComponent<RectTransform>();
                }
                return _selfRect;
            }
        }

        private void Update()
        {
            if (m_CalculateEveryFrame)
            {
                Adapt();
            }
        }

        public override void Adapt()
        {
            float sumGap = m_BiasAngle;
            for (int i = 0; i < SelfRect.childCount; i++)
            {
                var item = SelfRect.GetChild(i) as RectTransform;
                var rotation = Quaternion.Euler(0, 0, sumGap);
                var position = new Vector2(m_Distance * Mathf.Cos((sumGap + 90) * Mathf.PI / 180f), m_Distance * Mathf.Sin((sumGap + 90) * Mathf.PI / 180f));

                // 同值写入跳过：Transform 赋值不比较值，照写即每帧弄脏层级（内容没变也一样）。
                // 命中即当前值已等于目标，跳过与写入等价；被外部转动/挪动时值不等，照旧写回。
                if (item.localRotation != rotation)
                {
                    item.localRotation = rotation;
                }

                if ((Vector2)item.localPosition != position)
                {
                    item.localPosition = position;
                }

                if (m_Clockwise)
                {
                    sumGap -= m_Gap;
                }
                else
                {
                    sumGap += m_Gap;
                }
            }
        }
    }
}