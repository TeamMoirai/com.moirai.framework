using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if HYBRIDCLR_INSTALLED
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
#endif
#if OBFUZ_INSTALLED
using Obfuz.Settings;
using Obfuz4HybridCLR;
#endif

namespace Moirai.Atropos.Editor
{
    public static class BuildDLLCommand
    {
        private const string HYBRIDCLR_ENABLE_SCRIPTING_DEFINE_SYMBOL = "ENABLE_HYBRIDCLR";
        private const string OBFUZ_ENABLE_SCRIPTING_DEFINE_SYMBOL = "ENABLE_OBFUZ";

        #region HybridCLR/Define Symbols
#if HYBRIDCLR_INSTALLED
    #if ENABLE_HYBRIDCLR
        /// <summary>
        /// 禁用HybridCLR宏定义。
        /// </summary>
        [MenuItem("HybridCLR/Define Symbols/Disable HybridCLR", false, 30)]
        public static void DisableHybridCLR()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(HYBRIDCLR_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            HybridCLR.Editor.SettingsUtil.Enable = false;
            UpdateSettingEditor.ForceUpdateAssemblies();
        }
    #else
        /// <summary>
        /// 开启HybridCLR宏定义。
        /// </summary>
        [MenuItem("HybridCLR/Define Symbols/Enable HybridCLR", false, 31)]
        public static void EnableHybridCLR()
        {
            // 先去判断安装了没
            var controller = new InstallerController();
            if (!controller.HasInstalledHybridCLR())
            {
                controller.InstallDefaultHybridCLR();
            }

            if (!HybridCLR.Editor.SettingsUtil.Enable)
            {
                HybridCLR.Editor.SettingsUtil.Enable = true;
            }

            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(HYBRIDCLR_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            ScriptingDefineSymbols.AddScriptingDefineSymbol(HYBRIDCLR_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            UpdateSettingEditor.ForceUpdateAssemblies();
        }
    #endif
#endif
        #endregion

        #region Obfuz/Define Symbols
#if OBFUZ_INSTALLED
    #if ENABLE_OBFUZ
        /// <summary>
        /// 禁用Obfuz宏定义。
        /// </summary>
        [MenuItem("Obfuz/Define Symbols/Disable Obfuz", false, 30)]
        public static void DisableObfuz()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(OBFUZ_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            ObfuzSettings.Instance.buildPipelineSettings.enable = false;
        }
    #else
        /// <summary>
        /// 开启Obfuz宏定义。
        /// </summary>
        [MenuItem("Obfuz/Define Symbols/Enable Obfuz", false, 31)]
        public static void EnableObfuz()
        {
            ScriptingDefineSymbols.RemoveScriptingDefineSymbol(OBFUZ_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            ScriptingDefineSymbols.AddScriptingDefineSymbol(OBFUZ_ENABLE_SCRIPTING_DEFINE_SYMBOL);
            ObfuzSettings.Instance.buildPipelineSettings.enable = true;
        }
    #endif
#endif
        #endregion

#if HYBRIDCLR_INSTALLED && ENABLE_HYBRIDCLR
        [MenuItem("HybridCLR/Build/BuildAssets And CopyTo AssemblyTextAssetPath")]
        public static void BuildAndCopyDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            CompileDllCommand.CompileDll(target);
            CopyAOTHotUpdateDlls(target);
        }

        public static void BuildAndCopyDlls(BuildTarget target)
        {
            CompileDllCommand.CompileDll(target);
            CopyAOTHotUpdateDlls(target);
        }
        
        public static void CopyAOTHotUpdateDlls(BuildTarget target)
        {
            CopyAOTAssembliesToAssetPath();
            CopyHotUpdateAssembliesToAssetPath();

#if OBFUZ_INSTALLED && ENABLE_OBFUZ
        CompileDllCommand.CompileDll(target);

        string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
        ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

        Directory.CreateDirectory(Application.streamingAssetsPath);

        string hotUpdateDllPath = $"{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}";
        List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();

        foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
        {
            string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedHotUpdateDllPath : hotUpdateDllPath;
            string srcFile = $"{srcDir}/{assName}.dll";
            string dstFile = Application.dataPath +"/"+ UpdateSettings.AssemblyTextAssetPath  + $"/{assName}.dll.bytes";
            if (File.Exists(srcFile))
            {
                File.Copy(srcFile, dstFile, true);
                Debug.Log($"[CompileAndObfuscate] Copy {srcFile} to {dstFile}");
            }
        }
#endif

            AssetDatabase.Refresh();
        }
        
        public static void CopyAOTAssembliesToAssetPath()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = Application.dataPath +"/"+ UpdateSettings.AssemblyTextAssetPath;

            foreach (var dll in UpdateSettings.AOTMetaAssemblies)
            {
                string srcDllPath = $"{aotAssembliesSrcDir}/{dll}";
                if (!System.IO.File.Exists(srcDllPath))
                {
                    Debug.LogError($"ab中添加AOT补充元数据dll:{srcDllPath} 时发生错误,文件不存在。裁剪后的AOT dll在BuildPlayer时才能生成，因此需要你先构建一次游戏App后再打包。");
                    continue;
                }
                string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(srcDllPath, dllBytesPath, true);
                Debug.Log($"[CopyAOTAssembliesToStreamingAssets] copy AOT dll {srcDllPath} -> {dllBytesPath}");
            }
        }
        
        public static void CopyHotUpdateAssembliesToAssetPath()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;

            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = Application.dataPath +"/"+ UpdateSettings.AssemblyTextAssetPath;
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                System.IO.File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }
        }
#endif
    }
}