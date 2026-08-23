#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Moirai.Atropos.Editor
{
    public static class EditorSpriteSaveInfo
    {
        private const string ATLAS_CONFIG_PATH = "ProjectSettings/AtlasConfiguration.asset";
        private static readonly HashSet<string> _dirtyAtlasNames = new HashSet<string>();
        private static readonly Dictionary<string, List<string>> _atlasMap = new Dictionary<string, List<string>>();
        private static bool _initialized;
        private static bool _isInScanExistingSprites;

        private static AtlasConfiguration Config => AtlasConfiguration.instance;

        static EditorSpriteSaveInfo()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            ScanExistingSprites(false);
            _initialized = true;
        }

        [MenuItem("Tools/图集工具/立即重新生成变动的图集数据")]
        public static void ForceGenerateAll()
        {
            ForceGenerateAll(false);
        }

        /// <summary>
        /// 同步刷新图集。forceAll 为 true 时原地更新全部图集，并在全部成功后清理失效图集。
        /// </summary>
        public static void ForceGenerateAll(bool forceAll)
        {
            _isInScanExistingSprites = true;

            try
            {
                EditorUtility.DisplayProgressBar("生成图集", "正在初始化...", 0f);
                ClearCache();
                EditorUtility.DisplayProgressBar("生成图集", "扫描现有精灵...", 0.2f);
                ScanExistingSprites(false);

                EditorUtility.DisplayProgressBar("生成图集", "分析变更...", 0.4f);
                int current = 0;
                int total = _atlasMap.Count;

                foreach (var item in _atlasMap)
                {
                    current++;

                    if (total > 0)
                    {
                        EditorUtility.DisplayProgressBar("生成图集", $"检查图集变更 ({current}/{total})...",
                            0.4f + 0.2f * current / total);
                    }

                    if (forceAll || NeedsAtlasUpdate(item.Key, item.Value))
                    {
                        _dirtyAtlasNames.Add(item.Key);
                    }
                }

                EditorUtility.DisplayProgressBar("生成图集", "生成图集文件...", 0.6f);
                ProcessDirtyAtlases();
                DeleteStaleAtlases(_atlasMap.Keys);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _isInScanExistingSprites = false;
            }
        }

        /// <summary>
        /// 构建前同步刷新全部图集。保留现有图集资产与 GUID，仅删除已经没有来源的孤儿图集。
        /// </summary>
        public static void RefreshAllForBuild()
        {
            ForceGenerateAll(true);
        }

        private static void DeleteStaleAtlases(IEnumerable<string> expectedAtlasNames)
        {
            if (!Directory.Exists(Config.m_OutputAtlasDir))
            {
                return;
            }

            var expectedNames = new HashSet<string>(expectedAtlasNames, StringComparer.OrdinalIgnoreCase);
            string expectedExtension = Config.m_EnableV2 ? ".spriteatlasv2" : ".spriteatlas";
            string[] atlasFiles = Directory.GetFiles(Config.m_OutputAtlasDir, "*.spriteatlas",
                    SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Config.m_OutputAtlasDir, "*.spriteatlasv2",
                    SearchOption.AllDirectories))
                .ToArray();
            int deletedCount = 0;

            foreach (string atlasPath in atlasFiles)
            {
                bool hasExpectedExtension = string.Equals(Path.GetExtension(atlasPath), expectedExtension,
                    StringComparison.OrdinalIgnoreCase);

                if (hasExpectedExtension && expectedNames.Contains(Path.GetFileNameWithoutExtension(atlasPath)))
                {
                    continue;
                }

                DeleteAtlas(atlasPath.Replace('\\', '/'));
                deletedCount++;
            }

            if (deletedCount > 0)
            {
                Debug.Log($"已删除 {deletedCount} 个失效图集文件");
            }
        }

        public static void OnImportSprite(string assetPath, bool isCreateNew = false)
        {
            string atlasName = AddSpriteToMap(assetPath);

            if (string.IsNullOrEmpty(atlasName))
            {
                return;
            }

            // 已存在 Sprite 重新导入时同样刷新所属图集。
            MarkDirty(atlasName, isCreateNew);
            MarkParentAtlasesDirty(assetPath, isCreateNew);
        }

        public static void OnDeleteSprite(string assetPath, bool isCreateNew = true)
        {
            assetPath = assetPath.Replace("\\", "/");

            if (!ShouldProcess(assetPath))
            {
                return;
            }

            var atlasName = ResolveAtlasName(assetPath);

            if (string.IsNullOrEmpty(atlasName))
            {
                return;
            }

            if (_atlasMap.TryGetValue(atlasName, out var atlasList))
            {
                atlasList.Remove(assetPath);
            }

            MarkDirty(atlasName, isCreateNew);
            MarkParentAtlasesDirty(assetPath, isCreateNew);
        }

        private static string AddSpriteToMap(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");

            if (!ShouldProcess(assetPath))
            {
                return null;
            }

            string atlasName = ResolveAtlasName(assetPath);

            if (string.IsNullOrEmpty(atlasName))
            {
                return null;
            }

            if (!_atlasMap.TryGetValue(atlasName, out var atlasList))
            {
                atlasList = new List<string>();
                _atlasMap[atlasName] = atlasList;
            }

            if (!atlasList.Contains(assetPath))
            {
                atlasList.Add(assetPath);
            }

            return atlasName;
        }

        public static string ResolveAtlasName(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");
            var atlasName = GetAtlasName(assetPath);
            if (string.IsNullOrEmpty(atlasName))
            {
                return null;
            }

            if (CheckIsNeedGenerateSingleAtlas(assetPath))
            {
                atlasName = GetSingleAtlasName(assetPath);
            }
            else if (CheckIsNeedGenerateRootChildDirAtlas(assetPath))
            {
                atlasName = GetRootChildDirAtlasName(assetPath);
            }

            return atlasName;
        }

        public static void MarkParentAtlasesDirty(string assetPath, bool isCreateNew)
        {
            var currentPath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");

            if (string.IsNullOrEmpty(currentPath)) return;

            var tempRootDirArr = new List<string>(Config.m_SourceAtlasRootDir);
            tempRootDirArr.AddRange(Config.m_RootChildAtlasDir);

            foreach (var rootPath in tempRootDirArr)
            {
                var normalizedRoot = rootPath.Replace("\\", "/").TrimEnd('/');
                var tempCurrentPath = currentPath;

                if (!IsSameOrChildPath(tempCurrentPath, normalizedRoot))
                {
                    continue;
                }

                while (tempCurrentPath != null && IsSameOrChildPath(tempCurrentPath, normalizedRoot))
                {
                    var parentAtlasName = GetAtlasNameForDirectory(tempCurrentPath);

                    if (!string.IsNullOrEmpty(parentAtlasName) && _atlasMap.ContainsKey(parentAtlasName))
                    {
                        MarkDirty(parentAtlasName, isCreateNew);
                    }

                    tempCurrentPath = Path.GetDirectoryName(tempCurrentPath)?.Replace("\\", "/");
                }
            }
        }

        private static void OnUpdate()
        {
            if (_isInScanExistingSprites) return;

            if (_dirtyAtlasNames.Count > 0)
            {
                ProcessDirtyAtlases();
            }
        }

        private static void ProcessDirtyAtlases()
        {
            string[] atlasNames = _dirtyAtlasNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            int totalCount = atlasNames.Length;
            int processedCount = 0;
            bool showProgress = totalCount > 3 && _isInScanExistingSprites;

            try
            {
                foreach (string atlasName in atlasNames)
                {
                    if (showProgress)
                    {
                        processedCount++;
                        EditorUtility.DisplayProgressBar("生成图集", $"更新图集: {atlasName} ({processedCount}/{totalCount})",
                            0.6f + 0.4f * processedCount / totalCount);
                    }

                    GenerateAtlas(atlasName);
                    _dirtyAtlasNames.Remove(atlasName);
                }
            }
            finally
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static void GenerateAtlas(string atlasName)
        {
            var outputPath = $"{Config.m_OutputAtlasDir}/{atlasName}.spriteatlas";
            var outputPathV2 = outputPath.Replace(".spriteatlas", ".spriteatlasv2");
            string deletePath = outputPath;

            if (Config.m_EnableV2)
            {
                DeleteAtlas(outputPath);
                deletePath = outputPathV2;
            }
            else
            {
                DeleteAtlas(outputPathV2);
                deletePath = outputPath;
            }

            var sprites = LoadValidSprites(atlasName);
            EnsureOutputDirectory();

            if (sprites.Count == 0)
            {
                DeleteAtlas(deletePath);
                return;
            }

            InternalGenerateAtlas(atlasName, sprites, outputPath);
        }

        private static string InternalGenerateAtlas(string atlasName, List<Sprite> sprites, string outputPath)
        {
            SpriteAtlasAsset spriteAtlasAsset = null;
            SpriteAtlas atlas = null;

            if (Config.m_EnableV2)
            {
                outputPath = outputPath.Replace(".spriteatlas", ".spriteatlasv2");

                if (!File.Exists(outputPath))
                {
                    spriteAtlasAsset = new SpriteAtlasAsset();
                }
                else
                {
                    spriteAtlasAsset = SpriteAtlasAsset.Load(outputPath);
                    atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);

                    if (atlas != null)
                    {
                        var olds = atlas.GetPackables();

                        if (olds != null)
                        {
                            spriteAtlasAsset.Remove(olds);
                        }
                    }
                }
            }

            if (Config.m_EnableV2)
            {
                spriteAtlasAsset?.Add(sprites.ToArray());
                SpriteAtlasAsset.Save(spriteAtlasAsset, outputPath);
                AssetDatabase.ImportAsset(outputPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
#if UNITY_2022_1_OR_NEWER
                SpriteAtlasImporter sai = AssetImporter.GetAtPath(outputPath) as SpriteAtlasImporter;

                if (sai == null)
                {
                    throw new InvalidOperationException($"无法获取图集导入器: {outputPath}");
                }

                ConfigureAtlasV2Settings(sai);

                if (AssetDatabase.WriteImportSettingsIfDirty(outputPath))
                {
                    AssetDatabase.ImportAsset(outputPath,
                        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
#else
                ConfigureAtlasV2Settings(spriteAtlasAsset);
                SpriteAtlasAsset.Save(spriteAtlasAsset, outputPath);
                AssetDatabase.ImportAsset(outputPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
#endif
                atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
            }
            else
            {
                atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);

                if (atlas != null)
                {
                    var olds = atlas.GetPackables();

                    if (olds != null)
                    {
                        atlas.Remove(olds);
                    }

                    ConfigureAtlasSettings(atlas);
                    atlas.Add(sprites.ToArray());
                    atlas.SetIsVariant(false);
                }
                else
                {
                    atlas = new SpriteAtlas();
                    ConfigureAtlasSettings(atlas);
                    atlas.Add(sprites.ToArray());
                    atlas.SetIsVariant(false);
                    AssetDatabase.CreateAsset(atlas, outputPath);
                }
            }

            if (atlas != null)
            {
                EditorUtility.SetDirty(atlas);
            }

            if (Config.m_EnableLogging)
            {
                Debug.Log($"<b>[Generate Atlas]</b>: {atlasName} ({sprites.Count} sprites)");
            }

            return outputPath;
        }

        private static List<Sprite> LoadValidSprites(string atlasName)
        {
            if (_atlasMap.TryGetValue(atlasName, out List<string> spriteList))
            {
                var allSprites = new List<Sprite>();

                foreach (var assetPath in spriteList.Where(File.Exists))
                {
                    // 加载所有子图
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                        .OfType<Sprite>()
                        .Where(s => s != null)
                        .ToArray();

                    allSprites.AddRange(sprites);
                }

                return allSprites;
            }
            return new List<Sprite>();
        }


#if UNITY_2022_1_OR_NEWER
        private static void ConfigureAtlasV2Settings(SpriteAtlasImporter atlasImporter)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = atlasImporter.GetPlatformSettings(platform);
                if (settings == null) return;
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.m_CompressionQuality;
                atlasImporter.SetPlatformSettings(settings);
            }

            SetPlatform("Android", Config.m_AndroidFormat);
            SetPlatform("iPhone", Config.m_IOSFormat);
            SetPlatform("WebGL", Config.m_WebGLFormat);

            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.m_Padding,
                enableRotation = Config.m_EnableRotation,
                blockOffset = Config.m_BlockOffset,
                enableTightPacking = Config.m_TightPacking,
                enableAlphaDilation = true
            };
            atlasImporter.packingSettings = packingSettings;
        }
#else
        private static void ConfigureAtlasV2Settings(SpriteAtlasAsset spriteAtlasAsset)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = spriteAtlasAsset.GetPlatformSettings(platform);
                if (settings == null) return;
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.m_CompressionQuality;
                spriteAtlasAsset.SetPlatformSettings(settings);
            }

            SetPlatform("Android", Config.m_AndroidFormat);
            SetPlatform("iPhone", Config.m_IOSFormat);
            SetPlatform("WebGL", Config.m_WebGLFormat);

            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.m_Padding,
                enableRotation = Config.m_EnableRotation,
                blockOffset = Config.m_BlockOffset,
                enableTightPacking = Config.m_TightPacking,
                enableAlphaDilation = true
            };
            spriteAtlasAsset.SetPackingSettings(packingSettings);
        }
#endif


        private static void ConfigureAtlasSettings(SpriteAtlas atlas)
        {
            void SetPlatform(string platform, TextureImporterFormat format)
            {
                var settings = atlas.GetPlatformSettings(platform);
                settings.overridden = true;
                settings.format = format;
                settings.compressionQuality = Config.m_CompressionQuality;
                atlas.SetPlatformSettings(settings);
            }

            SetPlatform("Android", Config.m_AndroidFormat);
            SetPlatform("iPhone", Config.m_IOSFormat);
            SetPlatform("WebGL", Config.m_WebGLFormat);

            var packingSettings = new SpriteAtlasPackingSettings
            {
                padding = Config.m_Padding,
                enableRotation = Config.m_EnableRotation,
                blockOffset = Config.m_BlockOffset,
                enableTightPacking = Config.m_TightPacking,
            };
            atlas.SetPackingSettings(packingSettings);
        }

        private static string GetAtlasName(string assetPath)
        {
            var tempRootDirArr = new List<string>(Config.m_SourceAtlasRootDir);
            tempRootDirArr.AddRange(Config.m_RootChildAtlasDir);
            foreach (var rootPath in tempRootDirArr)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');

                if (!IsPathUnderRoot(assetPath, tempPath))
                {
                    continue;
                }

                var relativePath = assetPath.Substring(tempPath.Length + 1).Split('/');
                // 根目录下文本不处理
                if (relativePath.Length < 2)
                {
                    return null;
                }
                // 提取目录部分
                var directories = relativePath.Take(relativePath.Length - 1);
                var atlasNames = string.Join("_", directories);
                // 根目录文件名
                var rootFolderName = Path.GetFileName(tempPath);
                return $"{rootFolderName}_{atlasNames}";
            }
            return null;
        }

        private static string GetRootChildDirAtlasName(string spritePath)
        {
            foreach (var rootPath in Config.m_RootChildAtlasDir)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');

                if (!IsPathUnderRoot(spritePath, tempPath))
                {
                    continue;
                }

                string relativePath = spritePath.Substring(tempPath.Length + 1);
                int separatorIndex = relativePath.IndexOf('/');

                if (separatorIndex <= 0)
                {
                    return null;
                }

                string rootName = Path.GetFileName(tempPath);
                string directoryName = relativePath.Substring(0, separatorIndex);
                return $"{rootName}_{directoryName}";
            }

            return null;
        }

        private static string GetSingleAtlasName(string spritePath)
        {
            foreach (var rootPath in Config.m_SourceAtlasRootDir)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');

                if (!IsPathUnderRoot(spritePath, tempPath))
                {
                    continue;
                }

                var relativePath = spritePath.Substring(tempPath.Length + 1).Split('/');
                // 根目录下文本不处理
                if (relativePath.Length < 2)
                {
                    return null;
                }
                // 提取目录部分
                // var directories = relativePath.Take(relativePath.Length - 1);
                relativePath[^1] = Path.GetFileNameWithoutExtension(spritePath);
                var atlasNames = string.Join("_", relativePath);
                // 根目录文件名
                var rootFolderName = Path.GetFileName(tempPath);
                return $"{rootFolderName}_{atlasNames}";
            }
            return null;
        }

        private static string GetAtlasNameForDirectory(string directoryPath)
        {
            foreach (var rootPath in Config.m_SourceAtlasRootDir)
            {
                var normalizedRoot = rootPath.Replace("\\", "/").TrimEnd('/');

                if (!IsPathUnderRoot(directoryPath, normalizedRoot))
                {
                    continue;
                }

                string relativePath = directoryPath.Substring(normalizedRoot.Length + 1);
                string rootFolderName = Path.GetFileName(normalizedRoot);
                return $"{rootFolderName}_{relativePath.Replace('/', '_')}";
            }

            return null;
        }

        private static void ScanExistingSprites(bool markDirty = true)
        {
            var sprites = new HashSet<string>(AssetDatabase.FindAssets("t:sprite", Config.m_SourceAtlasRootDir));
            sprites.UnionWith(AssetDatabase.FindAssets("t:sprite", Config.m_RootChildAtlasDir));

            foreach (var guid in sprites)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                string atlasName = AddSpriteToMap(path);

                if (markDirty && !string.IsNullOrEmpty(atlasName))
                {
                    MarkDirty(atlasName);
                }
            }
        }

        private static bool ShouldProcess(string assetPath)
        {
            return IsImageFile(assetPath) && !IsExcluded(assetPath);
        }

        private static bool IsExcluded(string path)
        {
            return CheckIsExcludeFolder(path) //spritePath.StartsWith(Config.m_ExcludeFolder)
                   || Config.m_ExcludeKeywords.Any(key => path.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool CheckIsNeedGenerateSingleAtlas(string spritePath)
        {
            // 检查是否是需要排除的路径
            return !CheckIsExcludeFolder(spritePath) //spritePath.StartsWith(Config.m_ExcludeFolder)
                   && Config.m_SingleAtlasDir.Any(rootPath => IsPathUnderRoot(spritePath, rootPath));
        }

        private static bool CheckIsNeedGenerateRootChildDirAtlas(string spritePath)
        {
            // 检查是否是需要排除的路径
            return !CheckIsExcludeFolder(spritePath) //spritePath.StartsWith(Config.m_ExcludeFolder)
                   && Config.m_RootChildAtlasDir.Any(rootPath => IsPathUnderRoot(spritePath, rootPath));
        }

        private static bool CheckIsExcludeFolder(string assetPath)
        {
            foreach (var rootPath in AtlasConfiguration.instance.m_ExcludeFolder)
            {
                var tempPath = rootPath.Replace("\\", "/").TrimEnd('/');

                if (IsPathUnderRoot(assetPath, tempPath))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPathUnderRoot(string assetPath, string rootPath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(rootPath))
            {
                return false;
            }

            string normalizedPath = assetPath.Replace("\\", "/");
            string normalizedRoot = rootPath.Replace("\\", "/").TrimEnd('/');
            return normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameOrChildPath(string assetPath, string rootPath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(rootPath))
            {
                return false;
            }

            string normalizedRoot = rootPath.Replace("\\", "/").TrimEnd('/');
            return string.Equals(assetPath.Replace("\\", "/"), normalizedRoot,
                       StringComparison.OrdinalIgnoreCase)
                   || IsPathUnderRoot(assetPath, normalizedRoot);
        }

        private static bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        private static void MarkDirty(string atlasName, bool isCreateNew = false)
        {
            if (string.IsNullOrEmpty(atlasName))
            {
                return;
            }

            _dirtyAtlasNames.Add(atlasName);
        }

        private static bool NeedsAtlasUpdate(string atlasName, IReadOnlyCollection<string> spritePaths)
        {
            string atlasPath = GetAtlasPath(atlasName);

            if (!File.Exists(atlasPath))
            {
                return true;
            }

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);

            if (atlas == null)
            {
                return true;
            }

            UnityEngine.Object[] packables = atlas.GetPackables();

            if (packables == null || packables.Any(packable => packable == null))
            {
                return true;
            }

            var actualPaths = new HashSet<string>(packables
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)), StringComparer.OrdinalIgnoreCase);
            var expectedPaths = new HashSet<string>(spritePaths, StringComparer.OrdinalIgnoreCase);
            return !actualPaths.SetEquals(expectedPaths)
                   || GetLatestAtlasTime(atlasPath) < GetLatestSpriteTime(spritePaths);
        }

        private static DateTime GetLatestSpriteTime(IEnumerable<string> spritePaths)
        {
            DateTime maxTime = GetLastWriteTimeUtc(ATLAS_CONFIG_PATH);

            foreach (string path in spritePaths)
            {
                maxTime = Max(maxTime, GetLastWriteTimeUtc(path));
                maxTime = Max(maxTime, GetLastWriteTimeUtc(path + ".meta"));
            }

            return maxTime;
        }

        private static DateTime GetLatestAtlasTime(string atlasPath)
        {
            return Max(GetLastWriteTimeUtc(atlasPath), GetLastWriteTimeUtc(atlasPath + ".meta"));
        }

        private static DateTime GetLastWriteTimeUtc(string path)
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        private static DateTime Max(DateTime left, DateTime right)
        {
            return left >= right ? left : right;
        }

        private static string GetAtlasPath(string atlasName)
        {
            string extension = Config.m_EnableV2 ? ".spriteatlasv2" : ".spriteatlas";
            return $"{Config.m_OutputAtlasDir}/{atlasName}{extension}";
        }

        private static void DeleteAtlas(string path)
        {
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
                if (Config.m_EnableLogging)
                    Debug.Log($"Deleted empty atlas: {Path.GetFileName(path)}");
            }
        }

        private static void EnsureOutputDirectory()
        {
            if (!Directory.Exists(Config.m_OutputAtlasDir))
            {
                Directory.CreateDirectory(Config.m_OutputAtlasDir);
            }
        }

        public static void ClearCache()
        {
            _dirtyAtlasNames.Clear();
            _atlasMap.Clear();
        }
    }
}
#endif