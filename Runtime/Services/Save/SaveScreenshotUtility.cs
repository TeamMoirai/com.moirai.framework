using System;
using System.IO;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档截图工具：屏幕捕获（运行态主线程）与缩略图 PNG 编码（纯函数核心，像素源可注入）。
    /// <para>管线：<see cref="ScreenCapture.CaptureScreenshotAsTexture()"/>（主线程，帧末捕获）→ CPU 盒式降采样（保纵横比、不放大）
    /// → <see cref="ImageConversion.EncodeToPNG"/> 主线程一次编码（小图成本可忽略；ImageConversion 为主线程约束 API）。</para>
    /// <para>降采样与编码收敛为单一 CPU 路径（非 GPU Blit 双路径），EditMode 下以注入像素源全链路覆盖。</para>
    /// </summary>
    internal static class SaveScreenshotUtility
    {
        /// <summary>截图 sidecar 文件名后缀（与存档文件同目录；经 ISaveStorage 落盘，云后端天然跟随）。</summary>
        internal const string SCREENSHOT_FILE_SUFFIX = ".screenshot.png";

        /// <summary>缩略图最长边默认值（像素）。</summary>
        internal const int DEFAULT_MAX_DIMENSION = 256;

        #region 命名 [NAMING]

        /// <summary>
        /// 解析存档文件名对应的截图 sidecar 文件名（去扩展名后追加 <see cref="SCREENSHOT_FILE_SUFFIX"/>）。
        /// </summary>
        /// <param name="fileName">存档文件名（已校验）。</param>
        /// <returns>截图 sidecar 文件名。</returns>
        internal static string DetermineScreenshotFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + SCREENSHOT_FILE_SUFFIX;
        }

        #endregion

        #region 屏幕捕获 [CAPTURE]

        /// <summary>
        /// 捕获当前帧屏幕像素（仅限运行态主线程，须在 <c>WaitForEndOfFrame</c> 之后调用）。
        /// </summary>
        /// <param name="width">输出截图宽度（像素）。</param>
        /// <param name="height">输出截图高度（像素）。</param>
        /// <returns>屏幕像素数组（RGBA32，行主序）。</returns>
        internal static Color32[] CaptureScreenPixels(out int width, out int height)
        {
            Texture2D captured = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                width = captured.width;
                height = captured.height;
                return captured.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(captured);
            }
        }

        #endregion

        #region 降采样与编码 [DOWNSAMPLE / ENCODE]

        /// <summary>
        /// 计算缩略图尺寸（保纵横比、最长边钳制到 <paramref name="maxDimension"/>、不放大、最小 1 像素）。
        /// </summary>
        /// <param name="sourceWidth">源宽度（像素，&gt; 0）。</param>
        /// <param name="sourceHeight">源高度（像素，&gt; 0）。</param>
        /// <param name="maxDimension">最长边上限（像素，&gt; 0）。</param>
        /// <param name="thumbnailWidth">输出缩略图宽度。</param>
        /// <param name="thumbnailHeight">输出缩略图高度。</param>
        internal static void ComputeThumbnailSize(int sourceWidth, int sourceHeight, int maxDimension, out int thumbnailWidth, out int thumbnailHeight)
        {
            if (sourceWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source width must be positive.");
            }

            if (sourceHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceHeight), "Source height must be positive.");
            }

            if (maxDimension <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDimension), "Max dimension must be positive.");
            }

            int longest = Math.Max(sourceWidth, sourceHeight);
            if (longest <= maxDimension)
            {
                thumbnailWidth = sourceWidth;
                thumbnailHeight = sourceHeight;
                return;
            }

            // 整数等比收缩（先乘后除避免精度损失；源尺寸远大于上限，无溢出风险——像素量级乘积远低于 int 上限）
            if (sourceWidth >= sourceHeight)
            {
                thumbnailWidth = maxDimension;
                thumbnailHeight = Math.Max(1, (int)((long)sourceHeight * maxDimension / sourceWidth));
            }
            else
            {
                thumbnailHeight = maxDimension;
                thumbnailWidth = Math.Max(1, (int)((long)sourceWidth * maxDimension / sourceHeight));
            }
        }

        /// <summary>
        /// CPU 盒式降采样（面积平均；目标尺寸不高于源尺寸；输出写入 <paramref name="destination"/>）。
        /// </summary>
        /// <param name="source">源像素数组（RGBA32，行主序，长度 = <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/>）。</param>
        /// <param name="sourceWidth">源宽度（像素）。</param>
        /// <param name="sourceHeight">源高度（像素）。</param>
        /// <param name="destination">目标像素数组（长度 = <paramref name="thumbnailWidth"/> × <paramref name="thumbnailHeight"/>）。</param>
        /// <param name="thumbnailWidth">目标宽度（像素，≤ 源宽）。</param>
        /// <param name="thumbnailHeight">目标高度（像素，≤ 源高）。</param>
        internal static void DownsampleBox(Color32[] source, int sourceWidth, int sourceHeight, Color32[] destination, int thumbnailWidth, int thumbnailHeight)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (source.Length != sourceWidth * sourceHeight)
            {
                throw new ArgumentException("Source pixel count does not match its dimensions.", nameof(source));
            }

            if (destination.Length != thumbnailWidth * thumbnailHeight)
            {
                throw new ArgumentException("Destination pixel count does not match its dimensions.", nameof(destination));
            }

            if (thumbnailWidth <= 0 || thumbnailWidth > sourceWidth || thumbnailHeight <= 0 || thumbnailHeight > sourceHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(thumbnailWidth), "Thumbnail dimensions must be positive and no larger than the source.");
            }

            for (int y = 0; y < thumbnailHeight; y++)
            {
                // 目标像素映射的源矩形（整数边界，[y0, y1) 必非空——目标不高源）
                int y0 = y * sourceHeight / thumbnailHeight;
                int y1 = (y + 1) * sourceHeight / thumbnailHeight;
                for (int x = 0; x < thumbnailWidth; x++)
                {
                    int x0 = x * sourceWidth / thumbnailWidth;
                    int x1 = (x + 1) * sourceWidth / thumbnailWidth;

                    int sumR = 0;
                    int sumG = 0;
                    int sumB = 0;
                    int sumA = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int rowOffset = sy * sourceWidth;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            Color32 pixel = source[rowOffset + sx];
                            sumR += pixel.r;
                            sumG += pixel.g;
                            sumB += pixel.b;
                            sumA += pixel.a;
                        }
                    }

                    int count = (x1 - x0) * (y1 - y0);
                    int half = count >> 1;
                    destination[y * thumbnailWidth + x] = new Color32(
                        (byte)((sumR + half) / count),
                        (byte)((sumG + half) / count),
                        (byte)((sumB + half) / count),
                        (byte)((sumA + half) / count));
                }
            }
        }

        /// <summary>
        /// 将屏幕像素编码为缩略图 PNG（主线程：Texture2D/ImageConversion 为主线程约束 API；EditMode 可用，像素源注入测试走本路径）。
        /// </summary>
        /// <param name="pixels">源像素数组（RGBA32，行主序）。</param>
        /// <param name="width">源宽度（像素）。</param>
        /// <param name="height">源高度（像素）。</param>
        /// <param name="maxDimension">缩略图最长边上限（像素）。</param>
        /// <param name="thumbnailWidth">输出缩略图宽度。</param>
        /// <param name="thumbnailHeight">输出缩略图高度。</param>
        /// <returns>PNG 编码字节。</returns>
        internal static byte[] EncodeThumbnailPng(Color32[] pixels, int width, int height, int maxDimension, out int thumbnailWidth, out int thumbnailHeight)
        {
            ComputeThumbnailSize(width, height, maxDimension, out thumbnailWidth, out thumbnailHeight);

            Color32[] thumbnail = pixels;
            if (thumbnailWidth != width || thumbnailHeight != height)
            {
                thumbnail = new Color32[thumbnailWidth * thumbnailHeight];
                DownsampleBox(pixels, width, height, thumbnail, thumbnailWidth, thumbnailHeight);
            }

            var texture = new Texture2D(thumbnailWidth, thumbnailHeight, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(thumbnail);
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        #endregion
    }
}
