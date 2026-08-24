using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moirai.Atropos.Resource;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;
using BuildResult = UnityEditor.Build.Reporting.BuildResult;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 打包工具类。
    /// <remarks>通过 <see cref="CommandLineReader"/> 可以不前台开启 Unity 实现静默打包以及 CLI 工作流</remarks>
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// set WORKSPACE=.
    /// set UNITYEDITOR_PATH=G:/UnityEditor/2021.3.20f1c1/Editor
    /// set LOGFILE=./build.log
    /// set BUILDROOT=G:/UnityProject/Bundles
    ///
    /// %UNITYEDITOR_PATH%/Unity.exe %WORKSPACE%/UnityProject -logFile %LOGFILE% -executeMethod ReleaseTools.BuildPackage -quit -batchmode -CustomArgs:Language=en_US;Version=1.02;outputRoot=%BUILDROOT%
    ///
    /// @REM for /f "delims=[" %%i in (%LOGFILE%) do echo %%i
    ///
    /// pause
    /// ]]></code>
    /// </example>
    public static class ReleaseTools
    {
        #region CLI 入口 [CLI ENTRY]

        public static void BuildDll()
        {
#if HYBRIDCLR_INSTALLED
            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);
            BuildDLLCommand.BuildAndCopyDlls(target);
#endif
        }

        public static void BuildAssetBundle()
        {
            string outputRoot = CommandLineReader.GetCustomArgument("outputRoot");
            if (string.IsNullOrEmpty(outputRoot))
            {
                Debug.LogError($"Build Asset Bundle Error！outputRoot is null");
                return;
            }

            string packageVersion = CommandLineReader.GetCustomArgument("packageVersion");
            if (string.IsNullOrEmpty(packageVersion))
            {
                Debug.LogError($"Build Asset Bundle Error！packageVersion is null");
                return;
            }

            string platform = CommandLineReader.GetCustomArgument("platform");
            if (string.IsNullOrEmpty(platform))
            {
                Debug.LogError($"Build Asset Bundle Error！platform is null");
                return;
            }

            BuildTarget target = GetBuildTarget(platform);
            BuildInternal(target, outputRoot);
            Debug.LogWarning($"Start BuildPackage BuildTarget:{target} outputPath:{outputRoot}");
        }

        #endregion

        #region 菜单入口 [MENU ITEM ENTRY]

        [MenuItem("Tools/Build/一键打包AssetBundle _F8", false, 200)]
        // ReSharper disable once InconsistentNaming
        public static void BuildCurrentPlatformAB()
        {
            var config = BuildConfig.CreateDefault();
            config.m_ABOutputRoot = "./Bundles/";
            config.m_BuildHotFixDll = true;
            BuildWithConfig(config, buildPlayer: false);
        }

        [MenuItem("Tools/Build/一键打包Window", false, 100)]
        public static void AutomationBuild()
        {
            var config = BuildConfig.CreateDefault();
            config.m_BuildTarget = BuildTarget.StandaloneWindows64;
            config.m_ABOutputRoot = Application.dataPath + "/../Builds/Windows";
            config.m_BuildPlayer = true;
            config.m_PlayerPlatform = BuildTarget.StandaloneWindows64;
            config.m_PlayerOutputPath = $"{Application.dataPath}/../Build/Windows/Release_Windows.exe";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("Tools/Build/一键打包Android", false, 100)]
        public static void AutomationBuildAndroid()
        {
            var config = BuildConfig.CreateDefault();
            config.m_BuildTarget = BuildTarget.Android;
            config.m_ABOutputRoot = Application.dataPath + "/../Bundles";
            config.m_BuildPlayer = true;
            config.m_PlayerPlatform = BuildTarget.Android;
            config.m_PlayerOutputPath =
                $"{Application.dataPath}/../Build/Android/{BuildConfig.GetDefaultPackageVersion()}Android.apk";
            BuildWithConfig(config, buildPlayer: true);
        }

        [MenuItem("Tools/Build/一键打包IOS", false, 100)]
        public static void AutomationBuildIOS()
        {
            var config = BuildConfig.CreateDefault();
            config.m_BuildTarget = BuildTarget.iOS;
            config.m_ABOutputRoot = Application.dataPath + "/../Bundles";
            config.m_BuildPlayer = true;
            config.m_PlayerPlatform = BuildTarget.iOS;
            config.m_PlayerOutputPath = $"{Application.dataPath}/../Build/IOS/XCode_Project";
            BuildWithConfig(config, buildPlayer: true);
        }

        #endregion

        #region 参数化构建入口 [PARAM BUILD ENTRY]

        /// <summary>
        /// 通过 BuildConfig 执行完整构建流程
        /// </summary>
        public static void BuildWithConfig(BuildConfig config, bool buildPlayer)
        {
            // 1. [可选] 编译热更DLL
            if (config.m_BuildHotFixDll)
            {
#if HYBRIDCLR_INSTALLED
                Debug.Log("[BuildWithConfig] 编译热更DLL...");
                BuildDLLCommand.BuildAndCopyDlls();
#endif
            }

            // 2. 刷新资源
            AssetDatabase.Refresh();

            // 3. 构建 AssetBundle
            var buildResult = BuildInternalWithConfig(config);
            if (!buildResult.Success)
            {
                Debug.LogError($"[BuildWithConfig] AssetBundle构建失败: {buildResult.ErrorInfo}");
                return;
            }

            Debug.Log($"[BuildWithConfig] AssetBundle构建成功: {buildResult.OutputPackageDirectory}");

            // 4. [最小包] 删除 StreamingAssets 中的 .bundle 文件
            if (config.m_MinimalPackage)
            {
                ProcessMinimalPackage(config.PackageVersion, config.m_RetainTags, buildResult.OutputPackageDirectory);
            }

            // 5. 刷新资源
            AssetDatabase.Refresh();

            // 7. [可选] 构建 Player
            if (buildPlayer || config.m_BuildPlayer)
            {
                BuildImp(
                    BuildConfig.GetBuildTargetGroup(config.m_PlayerPlatform),
                    config.m_PlayerPlatform,
                    config.m_PlayerOutputPath
                );
            }
        }

        #endregion

        #region AssetBundle 构建 [AB BUILD]

        private static YooAsset.Editor.BuildResult BuildInternalWithConfig(BuildConfig config)
        {
            Debug.Log($"开始构建 : {config.m_BuildTarget}");

            IBuildPipeline pipeline;
            BuildParameters buildParameters;

            if (config.m_BuildPipeline == EBuildPipeline.LegacyBuildPipeline)
            {
                var builtinBuildParameters = new LegacyBuildParameters();
                pipeline = new LegacyBuildPipeline();
                buildParameters = builtinBuildParameters;
                builtinBuildParameters.CompressOption = config.m_CompressOption;
            }
            else
            {
                var scriptableBuildParameters = new ScriptableBuildParameters();
                pipeline = new ScriptableBuildPipeline();
                buildParameters = scriptableBuildParameters;
                scriptableBuildParameters.CompressOption = config.m_CompressOption;
                scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName("DefaultPackage");
                scriptableBuildParameters.ReplaceAssetPathWithAddress = UpdateSettings.ReplaceAssetPathWithAddress;
            }

            string outputRoot = config.m_ABOutputRoot;
            if (!Path.IsPathRooted(outputRoot))
            {
                outputRoot = Path.Combine(Application.dataPath + "/../", outputRoot);
                outputRoot = Path.GetFullPath(outputRoot).Replace('\\', '/');
            }

            buildParameters.BuildOutputRoot = outputRoot;
            buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = config.m_BuildPipeline.ToString();
            buildParameters.BuildTarget = config.m_BuildTarget;
            buildParameters.BuildBundleType = (int)EBundleType.AssetBundle;
            buildParameters.PackageName = "DefaultPackage";
            buildParameters.PackageVersion = config.PackageVersion;
            buildParameters.VerifyBuildingResult = config.m_VerifyBuildingResult;
            buildParameters.EnableSharePackRule = config.m_EnableSharePackRule;
            buildParameters.FileNameStyle = config.m_FileNameStyle;
            buildParameters.BundledCopyOption = config.m_BundledCopyOption;
            buildParameters.BundledCopyParams = string.Empty;
            buildParameters.BundleEncryptor = config.m_EncryptorHandler?.CreateEncryptor();
            buildParameters.ClearBuildCacheFiles = config.m_ClearBuildCache;
            buildParameters.UseAssetDependencyDB = config.m_UseAssetDependencyDB;

            var result = pipeline.Run(buildParameters, true);
            return result;
        }

        /// <summary>
        /// 旧版 BuildInternal，供 CLI 入口兼容
        /// </summary>
        private static void BuildInternal(BuildTarget buildTarget, string outputRoot, string packageVersion = "1.0",
            EBuildPipeline buildPipeline = EBuildPipeline.ScriptableBuildPipeline)
        {
            Debug.Log($"开始构建 : {buildTarget}");

            IBuildPipeline pipeline = null;
            BuildParameters buildParameters = null;

            if (buildPipeline == EBuildPipeline.LegacyBuildPipeline)
            {
                LegacyBuildParameters builtinBuildParameters = new LegacyBuildParameters();
                pipeline = new LegacyBuildPipeline();
                buildParameters = builtinBuildParameters;
                builtinBuildParameters.CompressOption = ECompressOption.LZ4;
            }
            else
            {
                ScriptableBuildParameters scriptableBuildParameters = new ScriptableBuildParameters();
                pipeline = new ScriptableBuildPipeline();
                buildParameters = scriptableBuildParameters;
                scriptableBuildParameters.CompressOption = ECompressOption.LZ4;
                scriptableBuildParameters.BuiltinShadersBundleName = GetBuiltinShaderBundleName("DefaultPackage");
                scriptableBuildParameters.ReplaceAssetPathWithAddress = UpdateSettings.ReplaceAssetPathWithAddress;
            }

            buildParameters.BuildOutputRoot = outputRoot;
            buildParameters.BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = buildPipeline.ToString();
            buildParameters.BuildTarget = buildTarget;
            buildParameters.BuildBundleType = (int)EBundleType.AssetBundle;
            buildParameters.PackageName = "DefaultPackage";
            buildParameters.PackageVersion = packageVersion;
            buildParameters.VerifyBuildingResult = true;
            buildParameters.EnableSharePackRule = true;
            buildParameters.FileNameStyle = EFileNameStyle.BundleName_HashName;
            buildParameters.BundledCopyOption = EBundledCopyOption.ClearAndCopyAll;
            buildParameters.BundledCopyParams = string.Empty;
            buildParameters.BundleEncryptor = GetBundleEncryptorFromResourceServiceDriver();
            buildParameters.ClearBuildCacheFiles = false;
            buildParameters.UseAssetDependencyDB = true;

            var buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                Debug.Log($"构建成功 : {buildResult.OutputPackageDirectory}");
            }
            else
            {
                Debug.LogError($"构建失败 : {buildResult.ErrorInfo}");
            }
        }

        #endregion

        #region 最小包后处理 [MIN PACKAGE POSTPROCESS]

        /// <summary>
        /// 读取文件的文本数据
        /// </summary>
        public static string ReadAllText(string filePath)
        {
            if (File.Exists(filePath) == false)
            {
                return null;
            }

            return File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 最小包模式：删除 StreamingAssets 中不带保留 tag 的 .bundle 文件
        /// 使用构建输出的 BuildReport（JSON）获取 bundle 的 tag 信息
        /// </summary>
        public static void ProcessMinimalPackage(string packageVersion, string retainTags,
            string outputPackageDirectory)
        {
            string streamingRoot = BundleBuilderHelper.GetStreamingAssetsRoot();
            string packageName = "DefaultPackage";

            // 定位构建报告文件
            string reportFileName = YooAssetConfiguration.GetBuildReportFileName(packageName, packageVersion);
            string reportPath = $"{outputPackageDirectory}/{reportFileName}";

            if (!File.Exists(reportPath))
            {
                Debug.LogError($"[最小包] 未找到构建报告: {reportPath}，跳过最小包处理");
                return;
            }

            // 反序列化 BuildReport
            YooAsset.Editor.BuildReport buildReport;
            try
            {
                string jsonData = ReadAllText(reportPath);
                buildReport = YooAsset.Editor.BuildReport.Deserialize(jsonData);
            }
            catch (Exception e)
            {
                Debug.LogError($"[最小包] 反序列化构建报告失败: {e.Message}");
                return;
            }

            // 构建保留文件名集合
            HashSet<string> retainFileNames = new HashSet<string>();
            string[] retainTagArray = ParseRetainTags(retainTags);

            if (retainTagArray.Length > 0)
            {
                foreach (var bundleInfo in buildReport.BundleInfos)
                {
                    if (bundleInfo.Tags != null && HasTag(bundleInfo.Tags, retainTagArray))
                    {
                        retainFileNames.Add(bundleInfo.FileName);
                    }
                }

                Debug.Log($"[最小包] 保留 Tag: [{string.Join(", ", retainTagArray)}]，匹配 {retainFileNames.Count} 个 bundle");
            }

            // 扫描 StreamingAssets 下的 .bundle 文件
            if (!Directory.Exists(streamingRoot))
            {
                Debug.LogWarning($"[最小包] StreamingAssets 目录不存在: {streamingRoot}");
                return;
            }

            string[] bundleFiles = Directory.GetFiles(streamingRoot, "*.bundle", SearchOption.AllDirectories);
            int deletedCount = 0;
            int retainedCount = 0;

            foreach (var file in bundleFiles)
            {
                string fileName = Path.GetFileName(file);
                if (retainFileNames.Contains(fileName))
                {
                    retainedCount++;
                    Debug.Log($"[最小包] 保留: {fileName}");
                }
                else
                {
                    File.Delete(file);
                    deletedCount++;
                    Debug.Log($"[最小包] 删除: {fileName}");
                }
            }

            Debug.Log($"[最小包] 处理完成 - 删除 {deletedCount} 个 .bundle，保留 {retainedCount} 个 .bundle");

            // 删除空目录
            CleanEmptyDirectories(streamingRoot);
        }

        private static bool HasTag(string[] bundleTags, string[] matchTags)
        {
            foreach (var matchTag in matchTags)
            {
                foreach (var bundleTag in bundleTags)
                {
                    if (bundleTag == matchTag)
                        return true;
                }
            }

            return false;
        }

        private static string[] ParseRetainTags(string retainTags)
        {
            if (string.IsNullOrWhiteSpace(retainTags))
                return Array.Empty<string>();

            return retainTags
                .Split(',', '，') // 支持中英文逗号
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
        }

        private static void CleanEmptyDirectories(string rootPath)
        {
            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                CleanEmptyDirectories(dir);
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }

        #endregion

        #region Player 构建 [PLAYER BUILD]

        public static void BuildImp(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
            AssetDatabase.Refresh();

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Select(scene => scene.path).ToArray(),
                locationPathName = locationPathName,
                targetGroup = buildTargetGroup,
                target = buildTarget,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Build success: {summary.totalSize / 1024 / 1024} MB, {summary.outputPath}");
            }
            else
            {
                Debug.Log($"Build Failed" + summary.result);
            }
        }

        #endregion

        #region 工具方法 [UTILITY METHODS]

        private static BuildTarget GetBuildTarget(string platform)
        {
            BuildTarget target = BuildTarget.NoTarget;
            switch (platform)
            {
                case "Android":
                    target = BuildTarget.Android;
                    break;
                case "IOS":
                    target = BuildTarget.iOS;
                    break;
                case "Windows":
                    target = BuildTarget.StandaloneWindows64;
                    break;
                case "MacOS":
                    target = BuildTarget.StandaloneOSX;
                    break;
                case "Linux":
                    target = BuildTarget.StandaloneLinux64;
                    break;
                case "WebGL":
                    target = BuildTarget.WebGL;
                    break;
                case "Switch":
                    target = BuildTarget.Switch;
                    break;
                case "PS4":
                    target = BuildTarget.PS4;
                    break;
                case "PS5":
                    target = BuildTarget.PS5;
                    break;
            }

            return target;
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            var uniqueBundleName = BundleCollectorSettingData.Setting.UniqueBundleName;
            var packRuleResult = DefaultBundlePackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        /// <summary>
        /// 根据 ResourceServiceDriver 的 EncryptorHandler 获取对应的加密服务（旧版兼容）
        /// </summary>
        private static IBundleEncryptor GetBundleEncryptorFromResourceServiceDriver()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab GameEntry");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[BuildInternal] Failed to find GameEntry.prefab");
                return null;
            }

            var gameEntryPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var gameEntryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gameEntryPath);
            if (gameEntryPrefab == null)
            {
                Debug.LogWarning("[BuildInternal] Failed to load GameEntry.prefab");
                return null;
            }

            var resourceServiceDriver = gameEntryPrefab.GetComponentInChildren<ResourceServiceDriver>();
            if (resourceServiceDriver == null)
            {
                Debug.LogWarning("[BuildInternal] ResourceServiceDriver not found in GameEntry.prefab");
                return null;
            }

            var encryptorHandler = resourceServiceDriver.EncryptorHandler;
            Debug.Log($"[BuildInternal] Use EncryptorHandler from ResourceServiceDriver: {encryptorHandler?.GetType().Name ?? "None"}");

            return encryptorHandler?.CreateEncryptor();
        }

        private static string GetBuildPackageVersion()
        {
            int totalMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
            return DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        #endregion
    }
}