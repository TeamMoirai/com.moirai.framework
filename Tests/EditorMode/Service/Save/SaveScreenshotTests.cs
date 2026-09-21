using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// V3-P8 截图与元数据镜像测试：sidecar 命名、缩略图尺寸计算、盒式降采样、PNG 编码回读、
    /// sidecar 落盘/级联删除、元数据合并语义与回读、截图完成事件派发、非运行态降级。
    /// <para>捕获核心经像素源注入在 EditMode 主线程直测（Texture2D/ImageConversion 为纯 CPU 路径）；
    /// 截图运行态编排（帧末等待/屏幕捕获）不在 EditMode 覆盖范围内。</para>
    /// </summary>
    public class SaveScreenshotTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
        }

        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;
        private List<SaveScreenshotArgs> _screenshotEvents;

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-screenshot-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;

            _screenshotEvents = new List<SaveScreenshotArgs>();
            SaveService.ScreenshotCaptured += OnScreenshotCaptured;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.ScreenshotCaptured -= OnScreenshotCaptured;
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveServiceHandler.s_OverrideBasePath = null;
            SetFacadeHandler(null);
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, true);
                }
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响测试结论
            }
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        private void OnScreenshotCaptured(SaveScreenshotArgs args) => _screenshotEvents.Add(args);

        /// <summary>
        /// 直设外观处理器字段（生成的 Handler 属性 setter 拒绝 null，降级注入走反射）。
        /// </summary>
        private static void SetFacadeHandler(SaveServiceHandler handler)
        {
            typeof(SaveService).GetField("s_Handler", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, handler);
        }

        /// <summary>
        /// 为随后一条 Warning 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectWarningLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Warning, new Regex(".*"));
            }
        }

        #region 命名 [NAMING]

        [Test]
        public void DetermineScreenshotFileName_StripsExtensionAndAppendsSuffix()
        {
            Assert.AreEqual("slot1.screenshot.png", SaveScreenshotUtility.DetermineScreenshotFileName("slot1.sav"));
            Assert.AreEqual("slot1.screenshot.png", SaveScreenshotUtility.DetermineScreenshotFileName("slot1"));
            Assert.AreEqual("存档槽位.screenshot.png", SaveScreenshotUtility.DetermineScreenshotFileName("存档槽位.sav"));
        }

        #endregion

        #region 尺寸计算 [SIZING]

        [Test]
        public void ComputeThumbnailSize_Landscape_ClampsLongestEdge()
        {
            SaveScreenshotUtility.ComputeThumbnailSize(1920, 1080, 256, out int width, out int height);
            Assert.AreEqual(256, width);
            Assert.AreEqual(144, height);
        }

        [Test]
        public void ComputeThumbnailSize_Portrait_ClampsHeight()
        {
            SaveScreenshotUtility.ComputeThumbnailSize(800, 1200, 256, out int width, out int height);
            Assert.AreEqual(170, width);
            Assert.AreEqual(256, height);
        }

        [Test]
        public void ComputeThumbnailSize_SmallerThanCap_KeepsSourceSize()
        {
            SaveScreenshotUtility.ComputeThumbnailSize(100, 50, 256, out int width, out int height);
            Assert.AreEqual(100, width);
            Assert.AreEqual(50, height);
        }

        [Test]
        public void ComputeThumbnailSize_ExtremeAspect_FloorsToOnePixel()
        {
            SaveScreenshotUtility.ComputeThumbnailSize(16, 1024, 256, out int width, out int height);
            Assert.AreEqual(4, width);
            Assert.AreEqual(256, height);
        }

        #endregion

        #region 降采样 [DOWNSAMPLE]

        [Test]
        public void DownsampleBox_UniformColor_PreservesColor()
        {
            var source = new Color32[16];
            var red = new Color32(200, 40, 10, 255);
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = red;
            }

            var destination = new Color32[4];
            SaveScreenshotUtility.DownsampleBox(source, 4, 4, destination, 2, 2);
            for (int i = 0; i < destination.Length; i++)
            {
                Assert.AreEqual(red, destination[i]);
            }
        }

        [Test]
        public void DownsampleBox_TwoByTwoToOne_AveragesWithRounding()
        {
            var source = new Color32[]
            {
                new Color32(0, 0, 0, 255),
                new Color32(255, 0, 0, 255),
                new Color32(0, 255, 0, 255),
                new Color32(255, 255, 255, 255),
            };

            var destination = new Color32[1];
            SaveScreenshotUtility.DownsampleBox(source, 2, 2, destination, 1, 1);
            Assert.AreEqual(new Color32(128, 128, 64, 255), destination[0]);
        }

        [Test]
        public void DownsampleBox_FourByFourToTwoByTwo_PicksQuadrants()
        {
            var source = new Color32[16];
            var topLeft = new Color32(255, 0, 0, 255);
            var topRight = new Color32(0, 255, 0, 255);
            var bottomLeft = new Color32(0, 0, 255, 255);
            var bottomRight = new Color32(255, 255, 255, 255);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool left = x < 2;
                    bool top = y < 2;
                    source[y * 4 + x] = top ? (left ? topLeft : topRight) : (left ? bottomLeft : bottomRight);
                }
            }

            var destination = new Color32[4];
            SaveScreenshotUtility.DownsampleBox(source, 4, 4, destination, 2, 2);
            Assert.AreEqual(topLeft, destination[0]);
            Assert.AreEqual(topRight, destination[1]);
            Assert.AreEqual(bottomLeft, destination[2]);
            Assert.AreEqual(bottomRight, destination[3]);
        }

        #endregion

        #region PNG 编码 [PNG ENCODE]

        /// <summary>
        /// 构造渐变测试像素源。
        /// </summary>
        private static Color32[] BuildGradientPixels(int width, int height)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32((byte)(x * 16), (byte)(y * 16), 128, 255);
                }
            }

            return pixels;
        }

        /// <summary>
        /// 解码 PNG 并返回尺寸（主线程 CPU 路径，EditMode 可用）。
        /// </summary>
        private static void DecodePngSize(byte[] pngBytes, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(texture, pngBytes), "PNG should decode.");
                width = texture.width;
                height = texture.height;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EncodeThumbnailPng_NoDownscale_RoundTripsDimensions()
        {
            byte[] png = SaveScreenshotUtility.EncodeThumbnailPng(BuildGradientPixels(4, 2), 4, 2, 4, out int outWidth, out int outHeight);
            Assert.AreEqual(4, outWidth);
            Assert.AreEqual(2, outHeight);
            Assert.AreEqual(0x89, png[0]);
            Assert.AreEqual((byte)'P', png[1]);
            Assert.AreEqual((byte)'N', png[2]);
            Assert.AreEqual((byte)'G', png[3]);
            DecodePngSize(png, out int decodedWidth, out int decodedHeight);
            Assert.AreEqual(4, decodedWidth);
            Assert.AreEqual(2, decodedHeight);
        }

        [Test]
        public void EncodeThumbnailPng_Downscales_RoundTripsDimensions()
        {
            byte[] png = SaveScreenshotUtility.EncodeThumbnailPng(BuildGradientPixels(8, 4), 8, 4, 2, out int outWidth, out int outHeight);
            Assert.AreEqual(2, outWidth);
            Assert.AreEqual(1, outHeight);
            DecodePngSize(png, out int decodedWidth, out int decodedHeight);
            Assert.AreEqual(2, decodedWidth);
            Assert.AreEqual(1, decodedHeight);
        }

        #endregion

        #region sidecar 落盘与级联 [SIDECAR IO]

        [Test]
        public void WriteScreenshot_PersistsSidecarBytes()
        {
            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths("slot7", TestFolder);
            _handler.SaveBlock(new SaveData { Gold = 1 }, "slot7", SaveServiceHandler.MAIN_BLOCK_KEY, TestFolder, ESaveBackend.Json, 1);

            byte[] png = SaveScreenshotUtility.EncodeThumbnailPng(BuildGradientPixels(4, 4), 4, 4, 4, out _, out _);
            _handler.WriteScreenshot(paths, png);

            string screenshotPath = SaveServiceHandler.ResolveScreenshotPath(paths);
            Assert.IsTrue(File.Exists(screenshotPath));
            CollectionAssert.AreEqual(png, File.ReadAllBytes(screenshotPath));
            StringAssert.EndsWith("slot7.screenshot.png", screenshotPath);
        }

        [Test]
        public void DeleteSave_CascadesScreenshotSidecar()
        {
            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths("slot8", TestFolder);
            _handler.SaveBlock(new SaveData { Gold = 1 }, "slot8", SaveServiceHandler.MAIN_BLOCK_KEY, TestFolder, ESaveBackend.Json, 1);
            _handler.WriteScreenshot(paths, SaveScreenshotUtility.EncodeThumbnailPng(BuildGradientPixels(2, 2), 2, 2, 2, out _, out _));

            string screenshotPath = SaveServiceHandler.ResolveScreenshotPath(paths);
            Assert.IsTrue(File.Exists(paths.SaveFilePath));
            Assert.IsTrue(File.Exists(screenshotPath));

            _handler.DeleteSave("slot8", TestFolder);
            Assert.IsFalse(File.Exists(paths.SaveFilePath));
            Assert.IsFalse(File.Exists(screenshotPath));
        }

        #endregion

        #region 元数据镜像 [METADATA MIRROR]

        [Test]
        public void MergeScreenshotMetadata_FileNotFound_CreatesFreshMetadata()
        {
            SaveMetadata metadata = SaveService.MergeScreenshotMetadata(
                SaveResult<SaveMetadata>.Failure(SaveError.FileNotFound), "slot9.screenshot.png", "Level_01", out bool shouldWrite);

            Assert.IsTrue(shouldWrite);
            Assert.AreEqual("slot9.screenshot.png", metadata.ThumbnailFileName);
            Assert.AreEqual("Level_01", metadata.SceneName);
        }

        [Test]
        public void MergeScreenshotMetadata_ExistingMetadata_PreservesOtherFields()
        {
            var existing = new SaveMetadata { GameVersion = "1.2.3", SaveVersion = 4, PlayTimeTicks = 123456789L };
            SaveMetadata metadata = SaveService.MergeScreenshotMetadata(
                SaveResult<SaveMetadata>.Success(existing), "slot9.screenshot.png", "Level_02", out bool shouldWrite);

            Assert.IsTrue(shouldWrite);
            Assert.AreSame(existing, metadata);
            Assert.AreEqual("1.2.3", metadata.GameVersion);
            Assert.AreEqual(4, metadata.SaveVersion);
            Assert.AreEqual(123456789L, metadata.PlayTimeTicks);
            Assert.AreEqual("slot9.screenshot.png", metadata.ThumbnailFileName);
            Assert.AreEqual("Level_02", metadata.SceneName);
        }

        [Test]
        public void MergeScreenshotMetadata_CorruptedMetadata_SkipsWriteWithWarning()
        {
            ExpectWarningLogForUtf();
            SaveMetadata metadata = SaveService.MergeScreenshotMetadata(
                SaveResult<SaveMetadata>.Failure(SaveError.Corrupted), "slot9.screenshot.png", "Level_03", out bool shouldWrite);

            Assert.IsFalse(shouldWrite);
            Assert.IsNull(metadata);
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Warning));
        }

        [Test]
        public void MetadataMirror_RoundTripsThroughContainer()
        {
            _handler.SaveBlock(new SaveData { Gold = 42 }, "slot9", SaveServiceHandler.MAIN_BLOCK_KEY, TestFolder, ESaveBackend.Json, 1);

            SaveResult<SaveMetadata> loadResult = _handler.TryLoadBlock<SaveMetadata>("slot9", SaveServiceHandler.META_BLOCK_KEY, TestFolder);
            Assert.AreEqual(SaveError.FileNotFound, loadResult.Error);

            SaveMetadata metadata = SaveService.MergeScreenshotMetadata(loadResult, "slot9.screenshot.png", "Level_09", out bool shouldWrite);
            Assert.IsTrue(shouldWrite);
            _handler.SaveBlock(metadata, "slot9", SaveServiceHandler.META_BLOCK_KEY, TestFolder, ESaveBackend.Json, 1);

            SaveResult<SaveMetadata> reloaded = _handler.TryLoadBlock<SaveMetadata>("slot9", SaveServiceHandler.META_BLOCK_KEY, TestFolder);
            Assert.IsTrue(reloaded.IsSuccess);
            Assert.AreEqual("slot9.screenshot.png", reloaded.Data.ThumbnailFileName);
            Assert.AreEqual("Level_09", reloaded.Data.SceneName);
        }

        #endregion

        #region 事件与降级 [EVENT / FALLBACK]

        [Test]
        public void RaiseScreenshotCaptured_DispatchesOnMainThreadInline()
        {
            SaveService.RaiseScreenshotCaptured("slot10", TestFolder, "slot10.screenshot.png", 256, 144);

            Assert.AreEqual(1, _screenshotEvents.Count);
            SaveScreenshotArgs args = _screenshotEvents[0];
            Assert.AreEqual("slot10", args.FileName);
            Assert.AreEqual(TestFolder, args.FolderName);
            Assert.AreEqual("slot10.screenshot.png", args.ScreenshotFileName);
            Assert.AreEqual(256, args.Width);
            Assert.AreEqual(144, args.Height);
        }

        [Test]
        public void CaptureScreenshotAsync_WhenHandlerMissing_ReturnsHandlerNotReady()
        {
            SaveError error = SaveService.CaptureScreenshotAsync("slot11", TestFolder).GetAwaiter().GetResult();
            Assert.AreEqual(SaveError.HandlerNotReady, error);
            Assert.AreEqual(0, _screenshotEvents.Count);
        }

        [Test]
        public void CaptureScreenshotAsync_InEditMode_ReturnsNotSupported()
        {
            SetFacadeHandler(_handler);
            ExpectWarningLogForUtf();

            SaveError error = SaveService.CaptureScreenshotAsync("slot11", TestFolder).GetAwaiter().GetResult();
            Assert.AreEqual(SaveError.NotSupported, error);
            Assert.AreEqual(0, _screenshotEvents.Count);
        }

        #endregion
    }
}
