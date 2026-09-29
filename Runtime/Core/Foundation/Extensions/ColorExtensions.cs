using UnityEngine;

namespace Moirai.Atropos
{
    public static class ColorExtensions
    {
        /// <summary>
        /// 返回指定的两个最小值/最大值之间的随机颜色
        /// </summary>
        /// <param name="color"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static Color RandomColor(this Color color, Color min, Color max)
        {
            Color c = new Color()
            {
                r = RandomUtility.NextFloat(min.r, max.r),
                g = RandomUtility.NextFloat(min.g, max.g),
                b = RandomUtility.NextFloat(min.b, max.b),
                a = RandomUtility.NextFloat(min.a, max.a)
            };

            return c;
        }
        
        /// <summary>
        /// 颜色叠加模式。
        /// </summary>
        /// <remarks>
        /// <c>Tint</c>：HSV 转换保留原值再乘 alpha；<c>Multiply</c>：整色相乘（含 alpha）；
        /// <c>Replace</c>：完全替换；<c>ReplaceKeepAlpha</c>：替换颜色但忽略原 alpha；<c>Add</c>：与原色相加（含 alpha）。
        /// </remarks>
        public enum ColoringMode { Tint, Multiply, Replace, ReplaceKeepAlpha, Add }

        public static Color Colorize(this Color originalColor, Color targetColor, ColoringMode coloringMode, float lerpAmount = 1.0f)
        {
            Color resultColor = Color.white;
            switch (coloringMode)
            {
                case ColoringMode.Tint:
                {
                    float s_h, s_s, s_v, t_h, t_s, t_v;
                    Color.RGBToHSV(originalColor, out s_h, out s_s, out s_v);
                    Color.RGBToHSV(targetColor, out t_h, out t_s, out t_v);
                    resultColor = Color.HSVToRGB(t_h, t_s, s_v * t_v);
                    resultColor.a = originalColor.a * targetColor.a;
                }
                    break;
                case ColoringMode.Multiply:
                    resultColor = originalColor * targetColor;
                    break;
                case ColoringMode.Replace:
                    resultColor = targetColor;
                    break;
                case ColoringMode.ReplaceKeepAlpha:
                    resultColor = targetColor;
                    resultColor.a = originalColor.a;
                    break;
                case ColoringMode.Add:
                    resultColor = originalColor + targetColor;
                    break;
                default:
                    break;
            }
            return Color.Lerp(originalColor, resultColor, lerpAmount);
        }
    }
}