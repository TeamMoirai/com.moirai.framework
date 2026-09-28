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