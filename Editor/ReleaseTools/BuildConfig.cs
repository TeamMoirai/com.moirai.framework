using System;
using Moirai.Atropos.Resource;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Moirai.Atropos.Editor
{
    public class BuildConfig : ScriptableObject
    {
        // 基础设置
        public BuildTarget BuildTarget;
        public EBuildPipeline BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption CompressOption = ECompressOption.LZ4;

        [ProviderDropdown(label: "加密方式")]
        [SerializeReference] internal ResourceEncryptorHandler m_EncryptorHandler;

        public string PackageVersion = "";
        public string OutputRoot = "./Builds/";

        /// <summary>
        /// 资源加解密处理器。
        /// </summary>
        public ResourceEncryptorHandler EncryptorHandler
        {
            get => m_EncryptorHandler;
            set => m_EncryptorHandler = value;
        }

        // 最小包设置
        public bool MinimalPackage;
        public string RetainTags = "";

        // 高级设置
        public bool EnableSharePackRule = true;
        public bool UseAssetDependencyDB = true;
        public bool ClearBuildCache;
        public bool VerifyBuildingResult = true;
        public EBundledCopyOption BundledCopyOption = EBundledCopyOption.ClearAndCopyAll;
        public EFileNameStyle FileNameStyle = EFileNameStyle.BundleName_HashName;

        // 热更DLL设置
        public bool BuildHotFixDll = true;

        // 打包Player设置
        public bool BuildPlayer;
        public BuildTarget PlayerPlatform;
        public string PlayerOutputPath = "";

        public static BuildConfig CreateDefault()
        {
            var config = CreateInstance<BuildConfig>();
            config.BuildTarget = EditorUserBuildSettings.activeBuildTarget;
            config.PlayerPlatform = EditorUserBuildSettings.activeBuildTarget;
            config.PackageVersion = GetDefaultPackageVersion();
            config.OutputRoot = "./Builds/";
            config.PlayerOutputPath = GetDefaultPlayerOutputPath(EditorUserBuildSettings.activeBuildTarget);
            return config;
        }

        public static string GetDefaultPackageVersion()
        {
            int totalMinutes = System.DateTime.Now.Hour * 60 + System.DateTime.Now.Minute;
            return System.DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        public static string GetDefaultPlayerOutputPath(BuildTarget target)
        {
            string basePath = Application.dataPath + "/../Build/";
            return target switch
            {
                BuildTarget.StandaloneWindows64 => basePath + "Windows/Release_Windows.exe",
                BuildTarget.Android => basePath + $"Android/{GetDefaultPackageVersion()}Android.apk",
                BuildTarget.iOS => basePath + "IOS/XCode_Project",
                BuildTarget.StandaloneOSX => basePath + "MacOS/Release_MacOS.app",
                BuildTarget.StandaloneLinux64 => basePath + "Linux/Release_Linux",
                BuildTarget.WebGL => basePath + "WebGL",
                _ => basePath + target + "/Release"
            };
        }

        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows64 => BuildTargetGroup.Standalone,
                BuildTarget.StandaloneOSX => BuildTargetGroup.Standalone,
                BuildTarget.StandaloneLinux64 => BuildTargetGroup.Standalone,
                BuildTarget.Android => BuildTargetGroup.Android,
                BuildTarget.iOS => BuildTargetGroup.iOS,
                BuildTarget.WebGL => BuildTargetGroup.WebGL,
                BuildTarget.Switch => BuildTargetGroup.Switch,
                BuildTarget.PS4 => BuildTargetGroup.PS4,
                BuildTarget.PS5 => BuildTargetGroup.PS5,
                _ => BuildTargetGroup.Standalone
            };
        }
    }
}
