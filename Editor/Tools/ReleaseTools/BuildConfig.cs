using System;
using System.IO;
using Moirai.Atropos.Resource;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 打包配置（AssetBundle + Player）。
    /// <para>Inspector 绘制由 Odin 特性驱动（见 <see cref="BuildConfigEditor"/>），
    /// YooAsset/BuildTarget 枚举无本地化标签，各中文显示名集中在本文件维护。</para>
    /// </summary>
    public class BuildConfig : ScriptableObject
    {
        #region 基础设置 [BASIC]

        [FoldoutGroup("基础设置", true, 0)]
        [InfoBox("选择构建目标平台和基础参数。AB输出目录支持相对路径（相对于项目根目录）。")]
        [LabelText("目标平台")]
        [ValueDropdown(nameof(PlatformChoices))]
        [SerializeField] internal BuildTarget m_BuildTarget;

        [FoldoutGroup("基础设置")]
        [LabelText("构建管线")]
        [ValueDropdown(nameof(PipelineChoices))]
        [SerializeField] internal EBuildPipeline m_BuildPipeline = EBuildPipeline.ScriptableBuildPipeline;

        [FoldoutGroup("基础设置")]
        [LabelText("压缩方式")]
        [ValueDropdown(nameof(CompressChoices))]
        [SerializeField] internal ECompressOption m_CompressOption = ECompressOption.LZ4;

        [FoldoutGroup("基础设置")]
        [ProviderDropdown(label: "加密方式")]
        [SerializeReference] internal YooAssetEncryptorHandler m_EncryptorHandler;

        private string _packageVersion = "";

        /// <summary>
        /// 资源版本号
        /// </summary>
        [FoldoutGroup("基础设置")]
        [HorizontalGroup("基础设置/VersionRow")]
        [PropertyOrder(1)]
        [LabelText("资源版本号")]
        [ShowInInspector]
        public string PackageVersion
        {
            get => string.IsNullOrEmpty(_packageVersion) ? GetDefaultPackageVersion() : _packageVersion;
            set => _packageVersion = value;
        }

        [FoldoutGroup("基础设置")]
        [HorizontalGroup("基础设置/OutputRow")]
        [PropertyOrder(3)]
        [LabelText("AB输出目录")]
        // ReSharper disable once InconsistentNaming
        [SerializeField] internal string m_ABOutputRoot = "./Builds/";

        #endregion

        #region 最小包设置 [MINIMAL PACKAGE]

        [FoldoutGroup("最小包设置", true, 10)]
        [LabelText("启用最小包模式")]
        [Tooltip("构建后删除 StreamingAssets 中的 .bundle 文件")]
        [SerializeField] internal bool m_MinimalPackage;

        [FoldoutGroup("最小包设置")]
        [ShowIf(nameof(m_MinimalPackage))]
        [InfoBoxBelow("$" + nameof(MinimalHelpText))]
        [LabelText("保留Tag(逗号分隔)")]
        [Tooltip("带这些Tag的bundle不会被删除")]
        [SerializeField] internal string m_RetainTags = "";

        /// <summary>
        /// 最小包模式帮助文本（随保留Tag动态变化）。
        /// </summary>
        private string MinimalHelpText
        {
            get
            {
                string tagInfo = string.IsNullOrWhiteSpace(m_RetainTags)
                    ? "所有 .bundle 文件将被删除（仅保留清单）"
                    : $"保留带 [{m_RetainTags}] Tag 的 bundle，其余删除";

                return "最小包模式：删除 StreamingAssets 中所有 .bundle 文件，仅保留清单文件（.bytes/.hash/.version）。\n" +
                       $"当前: {tagInfo}\n\n" +
                       "适用于 HostPlayMode 在线下载资源的场景，可大幅减小首包体积。";
            }
        }

        #endregion

        #region 高级设置 [ADVANCED]

        [FoldoutGroup("高级设置", false, 20)]
        [LabelText("启用共享资源打包")]
        [Tooltip("自动提取共享资源到独立bundle")]
        [SerializeField] internal bool m_EnableSharePackRule = true;

        [FoldoutGroup("高级设置")]
        [LabelText("使用资源依赖数据库")]
        [Tooltip("提高打包速度")]
        [SerializeField] internal bool m_UseAssetDependencyDB = true;

        [FoldoutGroup("高级设置")]
        [LabelText("清理构建缓存(禁用增量构建)")]
        [Tooltip("全量重新构建")]
        [SerializeField] internal bool m_ClearBuildCache;

        [FoldoutGroup("高级设置")]
        [LabelText("验证构建结果")]
        [Tooltip("构建后验证资源完整性")]
        [SerializeField] internal bool m_VerifyBuildingResult = true;

        [FoldoutGroup("高级设置")]
        [LabelText("内置文件拷贝")]
        [ValueDropdown(nameof(CopyOptionChoices))]
        [SerializeField] internal EBundledCopyOption m_BundledCopyOption = EBundledCopyOption.ClearAndCopyAll;

        [FoldoutGroup("高级设置")]
        [LabelText("文件名风格")]
        [ValueDropdown(nameof(FileNameStyleChoices))]
        [SerializeField] internal EFileNameStyle m_FileNameStyle = EFileNameStyle.BundleName_HashName;

        #endregion

        #region 热更DLL设置 [HOTFIX DLL]

        [FoldoutGroup("热更DLL设置", true, 30)]
        [LabelText("构建前编译热更DLL")]
        [Tooltip("执行 BuildDLLCommand.BuildAndCopyDlls")]
        [SerializeField] internal bool m_BuildHotFixDll = true;

        #endregion

        #region 打包Player设置 [PLAYER BUILD]

        [FoldoutGroup("打包Player设置", false, 40)]
        [LabelText("构建Player")]
        [Tooltip("构建可执行程序(exe/apk/ipa)")]
        [SerializeField] internal bool m_BuildPlayer;

        [FoldoutGroup("打包Player设置")]
        [ShowIf(nameof(m_BuildPlayer))]
        [LabelText("Player平台")]
        [ValueDropdown(nameof(PlatformChoices))]
        [SerializeField] internal BuildTarget m_PlayerPlatform;

        [FoldoutGroup("打包Player设置")]
        [HorizontalGroup("打包Player设置/PlayerOutputRow")]
        [ShowIf(nameof(m_BuildPlayer))]
        [LabelText("输出路径")]
        [SerializeField] internal string m_PlayerOutputPath = "";

        #endregion

        #region 枚举下拉项 [ENUM DROPDOWN CHOICES]

        private static ValueDropdownList<BuildTarget> PlatformChoices => new ValueDropdownList<BuildTarget>
        {
            { "Windows 64-bit", BuildTarget.StandaloneWindows64 },
            { "macOS", BuildTarget.StandaloneOSX },
            { "Linux", BuildTarget.StandaloneLinux64 },
            { "Android", BuildTarget.Android },
            { "iOS", BuildTarget.iOS },
            { "WebGL", BuildTarget.WebGL },
        };

        private static ValueDropdownList<EBuildPipeline> PipelineChoices => new ValueDropdownList<EBuildPipeline>
        {
            { "ScriptableBuildPipeline (SBP)", EBuildPipeline.ScriptableBuildPipeline },
            { "LegacyBuildPipeline (内置)", EBuildPipeline.LegacyBuildPipeline },
        };

        private static ValueDropdownList<ECompressOption> CompressChoices => new ValueDropdownList<ECompressOption>
        {
            { "Uncompressed (不压缩)", ECompressOption.Uncompressed },
            { "LZMA (高压缩)", ECompressOption.LZMA },
            { "LZ4 (快速压缩)", ECompressOption.LZ4 },
        };

        private static ValueDropdownList<EBundledCopyOption> CopyOptionChoices => new ValueDropdownList<EBundledCopyOption>
        {
            { "None (不拷贝)", EBundledCopyOption.None },
            { "ClearAndCopyAll (清空后拷贝全部)", EBundledCopyOption.ClearAndCopyAll },
            { "ClearAndCopyByTags (清空后按Tag拷贝)", EBundledCopyOption.ClearAndCopyByTags },
            { "OnlyCopyAll (仅拷贝全部)", EBundledCopyOption.OnlyCopyAll },
            { "OnlyCopyByTags (仅按Tag拷贝)", EBundledCopyOption.OnlyCopyByTags },
        };

        private static ValueDropdownList<EFileNameStyle> FileNameStyleChoices => new ValueDropdownList<EFileNameStyle>
        {
            { "HashName (哈希名)", EFileNameStyle.HashName },
            { "BundleName (资源包名称)", EFileNameStyle.BundleName },
            { "BundleName_HashName (资源包名称 + 哈希值名称)", EFileNameStyle.BundleName_HashName },
        };

        #endregion

        #region 编辑器操作 [EDITOR ACTIONS]

        [HorizontalGroup("基础设置/VersionRow", Width = 50f, MarginLeft = 4)]
        [PropertyOrder(2)]
        [Button("自动")]
        private void ApplyDefaultPackageVersion()
        {
            PackageVersion = GetDefaultPackageVersion();
        }

        [HorizontalGroup("基础设置/OutputRow", Width = 50f, MarginLeft = 4)]
        [PropertyOrder(4)]
        [Button("浏览")]
        private void BrowseOutputRoot()
        {
            string selected = EditorUtility.OpenFolderPanel("选择输出目录", m_ABOutputRoot, "");
            if (string.IsNullOrEmpty(selected)) return;

            string rel = PathGetRelative(Application.dataPath + "/../", selected);
            Undo.RecordObject(this, "Set Output Root");
            m_ABOutputRoot = string.IsNullOrEmpty(rel) ? selected : rel;
            EditorUtility.SetDirty(this);
        }

        [HorizontalGroup("打包Player设置/PlayerOutputRow", Width = 50f, MarginLeft = 4)]
        [PropertyOrder(1)]
        [ShowIf(nameof(m_BuildPlayer))]
        [Button("浏览")]
        private void BrowsePlayerOutputPath()
        {
            string selected = EditorUtility.SaveFilePanel("选择输出路径",
                Path.GetDirectoryName(m_PlayerOutputPath),
                Path.GetFileName(m_PlayerOutputPath), "");
            if (string.IsNullOrEmpty(selected)) return;

            Undo.RecordObject(this, "Set Player Output Path");
            m_PlayerOutputPath = selected;
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// 将绝对路径转为项目相对路径；失败（跨盘符等）返回空串。
        /// </summary>
        private static string PathGetRelative(string relativeTo, string path)
        {
            try
            {
                var uri = new Uri(relativeTo + "/");
                string rel = Uri.UnescapeDataString(uri.MakeRelativeUri(new Uri(path)).ToString());
                return rel.Replace('/', '\\');
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region 工厂与默认值 [FACTORY & DEFAULTS]

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

        #endregion
    }
}
