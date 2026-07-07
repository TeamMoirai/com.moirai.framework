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
                r = UnityEngine.Random.Range(min.r, max.r),
                g = UnityEngine.Random.Range(min.g, max.g),
                b = UnityEngine.Random.Range(min.b, max.b),
                a = UnityEngine.Random.Range(min.a, max.a)
            };

            return c;
        }
        
        /// <summary>
        /// Tint：使用HSV颜色转换，保留原始值，乘以alpha
        /// Multiply：整个颜色（包括 alpha）比原始颜色相乘
        /// Replace：用目标颜色完全替换原色
        /// ReplaceKeepAlpha：颜色被替换，但原来的 Alpha 通道被忽略
        /// Add：添加目标颜色（包括其 alpha）
        /// </summary>
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