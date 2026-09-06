using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.UI.Adapter
{
    public class HorizontalAdapter : AdapterBase
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
            float sumWidth = 0;
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
                sumWidth += childRect.rect.width;

                if (activityCount > 1) sumWidth += Gap;

                _targetPos[i] = sumWidth - childRect.rect.width;
                childRect.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, _targetPos[i], childRect.rect.width);
            }
            SelfRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, sumWidth);
        }
    }
}
