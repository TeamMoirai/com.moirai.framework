using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    /// <summary>
    /// <see cref="Rect"/> 矩形拆分工具类。
    /// </summary>
    public static class RectUtils
    {
        /// <summary>
        /// 按指定高度将目标矩形拆分为上下两部分。
        /// </summary>
        /// <param name="targetRect">待拆分的目标矩形。</param>
        /// <param name="height">上半部分（<see cref="curRect"/>）的高度。</param>
        /// <returns>返回一个元组：curRect 为上方高度为 <paramref name="height"/> 的子矩形，leftRect 为下方剩余部分的子矩形。</returns>
        public static (Rect curRect, Rect leftRect) SplitHeightRect(Rect targetRect, float height)
        {
            Rect curRect = new Rect(targetRect)
            {
                height = height,
            };

            Rect leftRect = new Rect(targetRect)
            {
                y = curRect.y + curRect.height,
                height = targetRect.height - height,
            };

            return (
                curRect,
                leftRect
            );
        }

        /// <summary>
        /// 按指定宽度将目标矩形拆分为左右两部分；目标矩形宽度不大于 0 时，两部分均为零宽度矩形。
        /// </summary>
        /// <param name="targetRect">待拆分的目标矩形。</param>
        /// <param name="width">左侧部分（<see cref="curRect"/>）的期望宽度，实际宽度不超过目标矩形宽度。</param>
        /// <returns>返回一个元组：curRect 为左侧子矩形，leftRect 为右侧剩余部分的子矩形。</returns>
        public static (Rect curRect, Rect leftRect) SplitWidthRect(Rect targetRect, float width)
        {
            float totalWidth = targetRect.width;
            if (totalWidth <= 0)
            {
                Rect zeroRect = new Rect(targetRect)
                {
                    width = 0,
                };
                return (zeroRect, zeroRect);
            }

            float canUseWidth = Mathf.Min(totalWidth, width);

            Rect curRect = new Rect(targetRect)
            {
                width = canUseWidth,
            };

            Rect leftRect = new Rect(targetRect)
            {
                x = curRect.x + curRect.width,
                width = targetRect.width - canUseWidth,
            };

            return (
                curRect,
                leftRect
            );
        }
    }
}