using UnityEngine;

namespace Moirai.Atropos.UI.Adapter
{
    public class AngleAdapter : AdapterBase
    {
        [Header("间隙")]
        public float Gap = 0;
        [Header("是否每帧都计算")]
        public bool CalculateEveryFrame = true;
        [Header("顺时针")]
        public bool Clockwise = true;
        [Header("偏移")]
        public float BiasAngle = 0;
        [Header("圆心距离")]
        public float Distance;
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
            if (CalculateEveryFrame)
            {
                Adapt();
            }
        }

        public override void Adapt()
        {
            float sumGap = BiasAngle;
            for (int i = 0; i < SelfRect.childCount; i++)
            {
                var item = SelfRect.GetChild(i) as RectTransform;
                var rotation = Quaternion.Euler(0, 0, sumGap);
                var position = new Vector2(Distance * Mathf.Cos((sumGap + 90) * Mathf.PI / 180f), Distance * Mathf.Sin((sumGap + 90) * Mathf.PI / 180f));

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

                if (Clockwise)
                {
                    sumGap -= Gap;
                }
                else
                {
                    sumGap += Gap;
                }
            }
        }
    }
}