using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.UI.Adapter
{
    public class VerticalAdapter : AdapterBase
    {
        [Header("间隙")]
        public float Gap = 0;
        [Header("是否每帧都计算")]
        public bool CalculateEveryFrame = true;
        private readonly List<float> _targetPos = new List<float>();
        private readonly List<RectTransform> _childRects = new List<RectTransform>();
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
            float sumHeight = 0;
            int activityCount = 0;

            _childRects.Clear();
            for (int i = 0; i < SelfRect.childCount; i++)
            {
                Transform child = SelfRect.GetChild(i);
                if (child.gameObject.activeInHierarchy)
                {
                    _childRects.Add(child as RectTransform);
                }
            }

            for (int i = 0; i < _childRects.Count; i++)
            {
                if (i >= _targetPos.Count) _targetPos.Add(0);

                RectTransform childRect = _childRects[i];
                activityCount++;
                sumHeight += childRect.rect.height;

                if (activityCount > 1) sumHeight += Gap;

                _targetPos[i] = sumHeight - childRect.rect.height;

                // 同值写入跳过：uGUI 的布局写入不比较值，照写就会每帧弄脏布局重建（内容没变也重建）
                if (IsAlreadyApplied(childRect, _targetPos[i]))
                {
                    continue;
                }

                childRect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, _targetPos[i], childRect.rect.height);
            }

            // 自身尺寸同理：非拉伸轴时 sizeDelta 即尺寸，同值跳过；拉伸轴按原路径写（sizeDelta 语义不同）
            if (SelfRect.anchorMin.y != SelfRect.anchorMax.y || SelfRect.sizeDelta.y != sumHeight)
            {
                SelfRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, sumHeight);
            }
        }

        /// <summary>
        /// 顶端贴边目标是否已就位。
        /// </summary>
        /// <remarks>
        /// 判据取自本版本 <c>SetInsetAndSizeFromParentEdge(Edge.Top, …)</c> 的实测后置条件： <c>anchorMin/Max.y = 1</c>、 <br />
        /// <c>sizeDelta.y = size</c>、<c>offsetMax.y = -inset</c>（<c>offsetMax</c> 相对父级顶边，与 pivot 无关）。 <br />
        /// 命中即当前可见状态已等于目标，跳过与写入等价；被外部挪动时 <c>offsetMax</c> 不等，照旧写回。
        /// </remarks>
        private static bool IsAlreadyApplied(RectTransform child, float inset)
        {
            return child.anchorMin.y == 1f
                && child.anchorMax.y == 1f
                && child.sizeDelta.y == child.rect.height
                && child.offsetMax.y == -inset;
        }
    }
}
