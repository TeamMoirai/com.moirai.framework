using System;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 这个类封装了所有跟Unity相关的工具函数
    /// </summary>
    public static partial class UnityUtility
    {
        #region 应用程序 [APPLICATION]

        /// <summary>
        /// 退出。editor停止播放，runtime则退出游戏
        /// </summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        #endregion

        #region Unity 组件 [UNITY COMPONENT]

        /// <summary>
        /// 对unity对象进行升序排序
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <typeparam name="K">排序的值</typeparam>
        /// <param name="comps">传入的组件数组</param>
        /// <param name="handler">处理的方法</param>
        public static void SortCompsByAscending<T, K>(T[] comps, Func<T, K> handler)
            where K : IComparable<K>
            where T : Component
        {
            AlgorithmUtility.SortByAscend(comps, handler);
            var length = comps.Length;
            for (int i = 0; i < length; i++)
            {
                comps[i].transform.SetSiblingIndex(i);
            }
        }

        /// <summary>
        /// 对unity对象进行降序排序
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <typeparam name="K">排序的值</typeparam>
        /// <param name="comps">传入的组件数组</param>
        /// <param name="handler">处理的方法</param>
        public static void SortCompsByDescending<T, K>(T[] comps, Func<T, K> handler)
            where K : IComparable<K>
            where T : Component
        {
            AlgorithmUtility.SortByDescend(comps, handler);
            var length = comps.Length;
            for (int i = 0; i < length; i++)
            {
                comps[i].transform.SetSiblingIndex(i);
            }
        }

        #endregion

        #region 图形 [GRAPHICS]

        /// <summary>
        /// 通过相机截取屏幕并转换为Texture2D
        /// </summary>
        /// <param name="camera">目标相机</param>
        /// <returns>相机抓取的屏幕Texture2D</returns>
        public static Texture2D CameraScreenshotAsTextureRGB(Camera camera)
        {
            return CameraScreenshotAsTexture(camera, TextureFormat.RGB565);
        }

        public static Texture2D CameraScreenshotAsTextureRGBA(Camera camera)
        {
            return CameraScreenshotAsTexture(camera, TextureFormat.RGBA32);
        }

        public static Texture2D CameraScreenshotAsTexture(Camera camera, TextureFormat textureFormat)
        {
            var oldRenderTexture = camera.targetTexture;
            var width = camera.pixelWidth;
            var height = camera.pixelHeight;
            var renderTexture = new RenderTexture(width, height, 24);
            camera.targetTexture = renderTexture;
            camera.Render();
            Texture2D texture2D = new Texture2D(width, height, textureFormat, false);
            RenderTexture.active = renderTexture;
            texture2D.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;
            camera.targetTexture = oldRenderTexture;
            return texture2D;
        }
        
        /// <summary>
        /// Texture旋转
        /// </summary>
        public static Texture2D RotateTexture(Texture2D texture, float eulerAngles)
        {
            int x;
            int y;
            int i;
            int j;
            float phi = eulerAngles / (180 / Mathf.PI);
            float sn = Mathf.Sin(phi);
            float cs = Mathf.Cos(phi);
            Color32[] arr = texture.GetPixels32();
            Color32[] arr2 = new Color32[arr.Length];
            int W = texture.width;
            int H = texture.height;
            int xc = W / 2;
            int yc = H / 2;

            for (j = 0; j < H; j++)
            {
                for (i = 0; i < W; i++)
                {
                    arr2[j * W + i] = new Color32(0, 0, 0, 0);

                    x = (int)(cs * (i - xc) + sn * (j - yc) + xc);
                    y = (int)(-sn * (i - xc) + cs * (j - yc) + yc);

                    if ((x > -1) && (x < W) && (y > -1) && (y < H))
                    {
                        arr2[j * W + i] = arr[y * W + x];
                    }
                }
            }

            Texture2D newImg = new Texture2D(W, H);
            newImg.SetPixels32(arr2);
            newImg.Apply();

            return newImg;
        }


        #endregion
        
        #region 数学 [MATH]
        
        /// <summary>
        /// 获取一个圆内随机点
        /// </summary>
        /// <param name="center">中心点</param>
        /// <param name="radius">半径</param>
        /// <returns>圆内随机点</returns>
        public static Vector2 GetRandomPointInCircle(Vector2 center, float radius)
        {
            if (radius < 0)
                radius = 0;
            var rndPtr = MathsUtility.RandomPointInsideUnitCircle() * radius;
            var rndPos = rndPtr + center;
            return rndPos;
        }

        /// <summary>
        /// 获取一个圆内随机点
        /// </summary>
        /// <param name="center">中心点</param>
        /// <param name="miniRadius">最小半径</param>
        /// <param name="maxRadius">最大半径</param>
        /// <returns>圆内随机点</returns>
        public static Vector2 GetRandomPointInCircle(Vector2 center, float miniRadius, float maxRadius)
        {
            if (miniRadius < 0)
                miniRadius = 0;
            if (maxRadius < miniRadius)
                maxRadius = miniRadius;
            var randomRadius = RandomUtility.NextFloat(miniRadius, maxRadius);
            var rndPtr = MathsUtility.RandomPointInsideUnitCircle() * randomRadius;
            var rndPos = rndPtr + center;
            return rndPos;
        }

        /// <summary>
        /// 获取一个球内随机点
        /// </summary>
        /// <param name="center">中心点</param>
        /// <param name="radius">半径</param>
        /// <returns>球内随机点</returns>
        public static Vector3 GetRandomPointInSphere(Vector3 center, float radius)
        {
            if (radius < 0)
                radius = 0;
            var rndPtr = MathsUtility.RandomPointInsideUnitSphere() * radius;
            var rndPos = rndPtr + center;
            return rndPos;
        }

        /// <summary>
        /// 获取一个球内随机点
        /// </summary>
        /// <param name="center">中心点</param>
        /// <param name="miniRadius">最小半径</param>
        /// <param name="maxRadius">最大半径</param>
        /// <returns>球内随机点</returns>
        public static Vector3 GetRandomPointInSphere(Vector3 center, float miniRadius, float maxRadius)
        {
            if (miniRadius < 0)
                miniRadius = 0;
            if (maxRadius < miniRadius)
                maxRadius = miniRadius;
            var randomRadius = RandomUtility.NextFloat(miniRadius, maxRadius);
            var rndPtr = MathsUtility.RandomPointInsideUnitSphere() * randomRadius;
            var rndPos = rndPtr + center;
            return rndPos;
        }
        
        /// <summary>
        /// 是否约等于另一个浮点数
        /// </summary>
        public static bool Approximately(float sourceValue, float targetValue)
        {
            return Mathf.Approximately(sourceValue, targetValue);
        }

        /// <summary>
        /// 限制一个向量在最大值与最小值之间
        /// </summary>
        public static Vector3 Clamp(Vector3 value, Vector3 min, Vector3 max)
        {
            value.x = Mathf.Clamp(value.x, min.x, max.x);
            value.y = Mathf.Clamp(value.y, min.y, max.y);
            value.z = Mathf.Clamp(value.z, min.z, max.z);
            return value;
        }

        public static Vector2 Clamp(Vector2 value, Vector2 min, Vector2 max)
        {
            value.x = Mathf.Clamp(value.x, min.x, max.x);
            value.y = Mathf.Clamp(value.y, min.y, max.y);
            return value;
        }

        /// <summary>
        /// 获得固定位数小数的向量
        /// </summary>
        public static Vector3 Round(Vector3 value, int decimals)
        {
            value.x = (float)Math.Round(value.x, decimals);
            value.y = (float)Math.Round(value.y, decimals);
            value.z = (float)Math.Round(value.z, decimals);
            return value;
        }

        /// <summary>
        /// 限制一个向量在最大值与最小值之间
        /// </summary>
        public static Vector3 Clamp(Vector3 value, float minX, float minY, float minZ, float maxX, float maxY,
            float maxZ)
        {
            value.x = Mathf.Clamp(value.x, minX, maxX);
            value.y = Mathf.Clamp(value.y, minY, maxY);
            value.z = Mathf.Clamp(value.z, minZ, maxZ);
            return value;
        }

        public static Vector2 Clamp(Vector2 value, float minX, float minY, float maxX, float maxY)
        {
            value.x = Mathf.Clamp(value.x, minX, maxX);
            value.y = Mathf.Clamp(value.y, minY, maxY);
            return value;
        }

        /// <summary>
        /// 获得固定位数小数的向量
        /// </summary>
        public static Vector2 Round(Vector2 value, int decimals)
        {
            value.x = (float)Math.Round(value.x, decimals);
            value.y = (float)Math.Round(value.y, decimals);
            return value;
        }

        #endregion

        #region 游戏对象 [GAME OBJECT]

        /// <summary>
        /// 通过类型查找任意活动的对象(实例)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="includeInactive">是否包含不活动对象</param>
        /// <returns></returns>
        public static T FindObjectByType<T>(bool includeInactive = false) where T : UObject
        {
            return
#if UNITY_2023_1_OR_NEWER
                UObject.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
#else
                UObject.FindObjectOfType<T>(includeInactive)
#endif
                ;
        }
        
        /// <summary>
        /// 通过类型查找活动的第一个对象(实例)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="includeInactive">是否包含不活动对象</param>
        /// <returns></returns>
        public static T FindFirstObjectByType<T>(bool includeInactive = false) where T : UObject
        {
            return
#if UNITY_2023_1_OR_NEWER
                UObject.FindFirstObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
#else
                UObject.FindObjectOfType<T>(includeInactive)
#endif
                ;
        }
        
        /// <summary>
        /// 通过类型查找所有活动的对象(实例)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="includeInactive">是否包含不活动对象</param>
        /// <returns></returns>
        public static T[] FindObjectsByType<T>(bool includeInactive = false) where T : UObject
        {
            return
#if UNITY_6000_4_OR_NEWER
                UObject.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
#elif UNITY_2023_1_OR_NEWER
                UObject.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None)
#else
				UObject.FindObjectsOfType<T>(includeInactive)
#endif
                ;
        }

        /// <summary>
        /// 通过类型查找活动的第一个对象(实例)
        /// </summary>
        /// <param name="classType">要查找的对象类型。</param>
        /// <param name="includeInactive">是否包含不活动对象</param>
        /// <returns></returns>
        public static UObject FindFirstObjectByType(Type classType, bool includeInactive = false)
        {
            return
#if UNITY_2023_1_OR_NEWER
                UObject.FindFirstObjectByType(classType, includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude)
#else
                UObject.FindObjectOfType(classType, includeInactive)
#endif
                ;
        }
        
        /// <summary>
        /// 通过类型查找所有活动的对象(实例)
        /// </summary>
        /// <param name="classType">要查找的对象类型。</param>
        /// <param name="includeInactive">是否包含不活动对象</param>
        /// <returns></returns>
        public static UObject[] FindObjectsByType(Type classType, bool includeInactive = false)
        {
            return
#if UNITY_2023_1_OR_NEWER
                UObject.FindObjectsByType(classType, includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None)
#else
				UObject.FindObjectsOfType(classType, includeInactive)
#endif
                ;
        }

        #endregion

        #region 其他 [OTHER]

        /// <summary>
        /// 获取对象的 EntityId。
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static int GetObjectEntityId(UObject target)
        {
            if (target == null) return 0;

#if UNITY_6000_4_OR_NEWER
            return target.GetEntityId().GetHashCode();
#else
            return target.GetInstanceID();
#endif
        }

        #endregion
    }
}
