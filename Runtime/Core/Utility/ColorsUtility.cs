using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class ColorsUtility
    {
        /// <summary>
        /// 从合理值（0-255）创建新的 Color
        /// </summary>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="a"></param>
        /// <returns></returns>
        public static Color CreateColor(int r, int g, int b, int a)
        {
	        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }
	    
        /// <summary>
        /// 从指定的颜色和 alpha 返回均匀渐变
        /// </summary>
        /// <param name="color">用于渐变两端的颜色</param>
        /// <param name="alpha">用于渐变两端的 alpha</param>
        /// <returns></returns>
        public static Gradient FlatGradient(Color32 color, float alpha = 1f)
        {
	        return new Gradient()
	        {
		        colorKeys = new GradientColorKey[2]
		        {
			        new GradientColorKey(color, 0), new GradientColorKey(color, 1f)
		        }, alphaKeys = new GradientAlphaKey[2]
		        {
			        new GradientAlphaKey(alpha, 0), new GradientAlphaKey(alpha, 1)
		        }
	        };
        }
        
        /// <summary>
        /// 返回由两种指定颜色和 alpha 组成的简单渐变
        /// </summary>
        /// <param name="startColor">用于渐变左侧的颜色</param>
        /// <param name="endColor">用于渐变右侧的颜色</param>
        /// <param name="startAlpha">用于渐变左侧的 alpha</param>
        /// <param name="endAlpha">	用于梯度右侧的 alpha</param>
        /// <returns></returns>
        public static Gradient SimpleGradient(Color32 startColor, Color32 endColor, float startAlpha = 1f,
	        float endAlpha = 1f)
        {
	        return new Gradient()
	        {
		        colorKeys = new GradientColorKey[2]
		        {
			        new GradientColorKey(startColor, 0), new GradientColorKey(endColor, 1f)
		        }, alphaKeys = new GradientAlphaKey[2]
		        {
			        new GradientAlphaKey(startAlpha, 0), new GradientAlphaKey(endAlpha, 1)
		        }
	        };
        }
        
        /// <summary>
        /// 返回两个渐变之间的线性插值
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="t"></param>
        /// <returns></returns>
	    public static Gradient LerpGradients(Gradient a, Gradient b, float t) 
		{
			Gradient result = new Gradient();
			
			GradientColorKey[] colorKeysA = a.colorKeys;
			GradientColorKey[] colorKeysB = b.colorKeys;
			int colorKeyCount = Mathf.Max(colorKeysA.Length, colorKeysB.Length);

			GradientColorKey[] resultColorKeys = new GradientColorKey[colorKeyCount];

			for (int i = 0; i < colorKeyCount; i++)
			{
				float time = i / (colorKeyCount - 1f); 
				Color colorA = a.Evaluate(time);
				Color colorB = b.Evaluate(time);
				resultColorKeys[i] = new GradientColorKey(Color.Lerp(colorA, colorB, t), time);
			}

			GradientAlphaKey[] alphaKeysA = a.alphaKeys;
			GradientAlphaKey[] alphaKeysB = b.alphaKeys;
			int alphaKeyCount = Mathf.Max(alphaKeysA.Length, alphaKeysB.Length);

			GradientAlphaKey[] resultAlphaKeys = new GradientAlphaKey[alphaKeyCount];

			for (int i = 0; i < alphaKeyCount; i++)
			{
				float time = i / (alphaKeyCount - 1f);
				float alphaA = a.Evaluate(time).a;
				float alphaB = b.Evaluate(time).a;
				resultAlphaKeys[i] = new GradientAlphaKey(Mathf.Lerp(alphaA, alphaB, t), time);
			}

			result.SetKeys(resultColorKeys, resultAlphaKeys);
			return result;
		}
    }
}