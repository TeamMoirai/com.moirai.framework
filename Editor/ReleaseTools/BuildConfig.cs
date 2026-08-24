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
        [SerializeField] internal BuildTarget m_BuildTarget;
        [SerializeField] internal EBuildPipeline m_BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        [SerializeField] internal ECompressOption m_CompressOption = ECompressOption.LZ4;

        [ProviderDropdown(label: "加密方式")]
        [SerializeReference] internal ResourceEncryptorHandler m_EncryptorHandler;

        // ReSharper disable once InconsistentNaming
        [SerializeField] internal string m_ABOutputRoot = "./Builds/";

        // 最小包设置
        [SerializeField] internal bool m_MinimalPackage;
        [SerializeField] internal string m_RetainTags = "";

        // 高级设置
        [SerializeField] internal bool m_EnableSharePackRule = true;
        [SerializeField] internal bool m_UseAssetDependencyDB = true;
        [SerializeField] internal bool m_ClearBuildCache;
        [SerializeField] internal bool m_VerifyBuildingResult = true;
        [SerializeField] internal EBundledCopyOption m_BundledCopyOption = EBundledCopyOption.ClearAndCopyAll;
        [SerializeField] internal EFileNameStyle m_FileNameStyle = EFileNameStyle.BundleName_HashName;

        // 热更DLL设置
        [SerializeField] internal bool m_BuildHotFixDll = true;

        // 打包Player设置
        [SerializeField] internal bool m_BuildPlayer;
        [SerializeField] internal BuildTarget m_PlayerPlatform;
        [SerializeField] internal string m_PlayerOutputPath = "";

        private string _packageVersion = "";
        /// <summary>资源版本号</summary>
        public string PackageVersion
        {
            get => string.IsNullOrEmpty(_packageVersion) ? GetDefaultPackageVersion() : _packageVersion;
            set => _packageVersion = value;
        }

        public static BuildConfig CreateDefault()
        {
            var config = CreateInstance<BuildConfig>();
            config.m_BuildTarget = EditorUserBuildSettings.activeBuildTarget;
            config.m_PlayerPlatform = EditorUserBuildSettings.activeBuildTarget;
            config.m_ABOutputRoot = "./Builds/";
            config.m_PlayerOutputPath = GetDefaultPlayerOutputPath(EditorUserBuildSettings.activeBuildTarget);
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
