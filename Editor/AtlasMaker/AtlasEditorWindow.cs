#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public class AtlasConfigWindow : EditorWindow
    {
        [MenuItem("Tools/图集工具/配置面板", false, 520)]
        public static void ShowWindow()
        {
            var window = GetWindow<AtlasConfigWindow>();
            window.titleContent = new GUIContent(" 图集配置窗口", EditorGUIUtility.IconContent("Settings").image);
            window.minSize = new Vector2(450, 400);
        }

        private Vector2 _scrollPosition;

        private int[] _paddingEnum = new int[] { 2, 4, 8 };
        private bool _showExcludeKeywords = false; // 新增折叠状态变量
        private bool _showSingleAtlasPath = false;
        private bool _showRootDirAtlasPath = false;
        private bool _showSourceAtlasRootPath = false;
        private bool _showExcludeAtlasPath = false;

        private static readonly string[] SPRITE_PACKER_MODE_NAMES =
        {
            "Disabled",
            "Sprite Atlas V1 - Enabled For Builds",
            "Sprite Atlas V1 - Always Enabled",
#if UNITY_2022_1_OR_NEWER
            "Sprite Atlas V2 - Enabled",
            "Sprite Atlas V2 - Enabled for Builds"
#else
            "Sprite Atlas V2 (Experimental) - Enabled"
#endif
        };

        private static readonly int[] SPRITE_PACKER_MODE_VALUES =
        {
            (int)SpritePackerMode.Disabled,
            (int)SpritePackerMode.BuildTimeOnlyAtlas,
            (int)SpritePackerMode.AlwaysOnAtlas,
            (int)SpritePackerMode.SpriteAtlasV2,
#if UNITY_2022_1_OR_NEWER
            (int)SpritePackerMode.SpriteAtlasV2Build
#endif
        };

        private static readonly string[] TEXTURE_COMPRESSION_NAMES =
        {
            "None",
            "Low Quality",
            "Normal Quality",
            "High Quality"
        };

        private static readonly int[] TEXTURE_COMPRESSION_VALUES =
        {
            (int)TextureImporterCompression.Uncompressed,
            (int)TextureImporterCompression.CompressedLQ,
            (int)TextureImporterCompression.Compressed,
            (int)TextureImporterCompression.CompressedHQ
        };

        private void OnGUI()
        {
            var config = AtlasConfiguration.Instance;

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollScope.scrollPosition;

                EditorGUI.BeginChangeCheck();

                DrawFolderSettings(config);
                DrawPlatformSettings(config);
                DrawPackingSettings(config);
                DrawSpriteImportSettings(config);
                DrawAdvancedSettings(config);

                if (EditorGUI.EndChangeCheck())
                {
                    AtlasConfiguration.Save(true);
                    AssetDatabase.Refresh();
                }

                DrawActionButtons();
            }
        }

        private void DrawSpriteImportSettings(AtlasConfiguration config)
        {
            EditorGUILayout.BeginVertical("box");
            var labelGUIContent = new GUIContent(" Sprite导入设置", EditorGUIUtility.IconContent("Sprite Icon").image);
            GUILayout.Label(labelGUIContent, EditorStyles.boldLabel, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            var checkMipmapsContent =
                new GUIContent(" 检查Mipmap导入设置", EditorGUIUtility.IconContent("LODGroup Icon").image);
            config.m_CheckMipmaps = EditorGUILayout.Toggle(checkMipmapsContent, config.m_CheckMipmaps);
            if (config.m_CheckMipmaps)
            {
                var enableMipmapsContent =
                    new GUIContent(" 允许Mipmap", EditorGUIUtility.IconContent("FilterByType").image);
                config.m_EnableMipmaps = EditorGUILayout.Toggle(enableMipmapsContent, config.m_EnableMipmaps);
            }

            var compressionContent = new GUIContent(" Compression", EditorGUIUtility.IconContent("Texture Icon").image);
            var textureCompressionValue = DrawIntPopup(compressionContent, (int)config.m_TextureCompression,
                TEXTURE_COMPRESSION_NAMES, TEXTURE_COMPRESSION_VALUES);
            config.m_TextureCompression = (TextureImporterCompression)textureCompressionValue;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawFolderSettings(AtlasConfiguration config)
        {
            EditorGUILayout.BeginVertical("box");
            var labelGUIContent = new GUIContent(" 目录设置", EditorGUIUtility.IconContent("Folder Icon").image);
            GUILayout.Label(labelGUIContent, EditorStyles.boldLabel, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            config.m_OutputAtlasDir = DrawFolderField("输出目录", "FolderOpened Icon", config.m_OutputAtlasDir);
            DrawPathArrItem("收集目录", "收集目录", "Collab.FolderAdded", ref config.m_SourceAtlasRootDir,
                ref _showSourceAtlasRootPath);
            DrawPathArrItem("排除目录", "排除目录", "Collab.FolderIgnored", ref config.m_ExcludeFolder,
                ref _showExcludeAtlasPath);
            DrawPathArrItem("以根目录的子级目录生成图集", "根目录", "Collab.FolderAdded", ref config.m_RootChildAtlasDir,
                ref _showRootDirAtlasPath);
            DrawPathArrItem("每张图都单独生成图集的目录", "单张图集目录", "Collab.FolderAdded", ref config.m_SingleAtlasDir,
                ref _showSingleAtlasPath);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawPathArrItem(string label, string itemLabel, string iconName, ref string[] paths,
            ref bool isShow)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            isShow = EditorGUILayout.BeginFoldoutHeaderGroup(isShow, label);
            // GUILayout.Label("", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            if (isShow)
            {
                GUILayout.Label("数量:", GUILayout.ExpandWidth(false));
                int newSize = EditorGUILayout.IntField(paths.Length, GUILayout.Width(40));
                newSize = Mathf.Max(0, newSize);
                if (newSize != paths.Length)
                {
                    Array.Resize(ref paths, newSize);
                }

                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), GUILayout.Width(25),
                        GUILayout.Height(20)))
                {
                    Array.Resize(ref paths, paths.Length + 1);
                }

                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus"), GUILayout.Width(25),
                        GUILayout.Height(20)) && paths.Length > 0)
                {
                    Array.Resize(ref paths, paths.Length - 1);
                }
            }

            EditorGUILayout.EndHorizontal();
            if (isShow)
            {
                EditorGUILayout.BeginVertical("box");
                for (int i = 0; i < paths.Length; i++)
                {
                    paths[i] = DrawFolderField($"{itemLabel}[{i}]", iconName, paths[i]);
                    // var keywordsContent = new GUIContent($" 关键词 [{i}]", EditorGUIUtility.IconContent("FilterByLabel").image);
                    // config.m_ExcludeKeywords[i] = EditorGUILayout.TextField(keywordsContent, config.m_ExcludeKeywords[i]);
                }

                GUILayout.Space(2);
                if (GUILayout.Button(new GUIContent(" 清空", EditorGUIUtility.IconContent("d_TreeEditor.Trash").image),
                        GUILayout.Height(25)))
                {
                    paths = Array.Empty<string>();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private string DrawFolderField(string label, string labelIcon, string path)
        {
            using var horizontalScope = new EditorGUILayout.HorizontalScope();
            var buttonGUIContent = new GUIContent("选择", EditorGUIUtility.IconContent("Folder Icon").image);
            var labelGUIContent = new GUIContent(" " + label, EditorGUIUtility.IconContent(labelIcon).image);
            path = EditorGUILayout.TextField(labelGUIContent, path);

            if (GUILayout.Button(buttonGUIContent, GUILayout.Width(60), GUILayout.Height(20)))
            {
                var newPath = EditorUtility.OpenFolderPanel(label, Application.dataPath, string.Empty);

                if (!string.IsNullOrEmpty(newPath) && newPath.StartsWith(Application.dataPath))
                {
                    path = "Assets" + newPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    Debug.LogError("路径不在Unity项目内: " + newPath);
                }
            }

            return path;
        }

        private void DrawPlatformSettings(AtlasConfiguration config)
        {
            EditorGUILayout.BeginVertical("box");
            var labelGUIContent =
                new GUIContent(" 平台设置", EditorGUIUtility.IconContent("BuildSettings.Standalone").image);
            GUILayout.Label(labelGUIContent, EditorStyles.boldLabel, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            var androidContent = new GUIContent(" Android 格式",
                EditorGUIUtility.IconContent("BuildSettings.Android.Small").image);
            config.m_AndroidFormat =
                (TextureImporterFormat)EditorGUILayout.EnumPopup(androidContent, config.m_AndroidFormat);
            var iosContent =
                new GUIContent(" iOS 格式", EditorGUIUtility.IconContent("BuildSettings.iPhone.Small").image);
            config.m_IOSFormat = (TextureImporterFormat)EditorGUILayout.EnumPopup(iosContent, config.m_IOSFormat);
            var webGLContent =
                new GUIContent(" WebGL 格式", EditorGUIUtility.IconContent("BuildSettings.WebGL.Small").image);
            config.m_WebGLFormat = (TextureImporterFormat)EditorGUILayout.EnumPopup(webGLContent, config.m_WebGLFormat);
            var compressionContent = new GUIContent(" 压缩质量", EditorGUIUtility.IconContent("MeshRenderer Icon").image);
            config.m_CompressionQuality =
                EditorGUILayout.IntSlider(compressionContent, config.m_CompressionQuality, 0, 100);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawPackingSettings(AtlasConfiguration config)
        {
            EditorGUILayout.BeginVertical("box");
            var labelGUIContent = new GUIContent(" 图集设置", EditorGUIUtility.IconContent("SpriteAtlas Icon").image);
            GUILayout.Label(labelGUIContent, EditorStyles.boldLabel, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            var spritePackerModeContent = new GUIContent(" Sprite Packer Mode",
                EditorGUIUtility.IconContent("SpriteAtlas Icon").image);
            var spritePackerModeValue = DrawIntPopup(spritePackerModeContent, (int)EditorSettings.spritePackerMode,
                SPRITE_PACKER_MODE_NAMES, SPRITE_PACKER_MODE_VALUES);
            var spritePackerMode = (SpritePackerMode)spritePackerModeValue;
            if (spritePackerMode != EditorSettings.spritePackerMode)
            {
                EditorSettings.spritePackerMode = spritePackerMode;
            }

            var paddingContent =
                new GUIContent(" Padding", EditorGUIUtility.IconContent("RectTransformBlueprint").image);
            config.m_Padding = DrawIntPopup(paddingContent, config.m_Padding,
                Array.ConvertAll(_paddingEnum, x => x.ToString()), _paddingEnum);
            var offsetContent = new GUIContent(" Block Offset", EditorGUIUtility.IconContent("MoveTool").image);
            config.m_BlockOffset = EditorGUILayout.IntField(offsetContent, config.m_BlockOffset);
            var rotationContent = new GUIContent(" Enable Rotation", EditorGUIUtility.IconContent("RotateTool").image);
            config.m_EnableRotation = EditorGUILayout.Toggle(rotationContent, config.m_EnableRotation);
            var tightPackingContent = new GUIContent(" 剔除透明区域", EditorGUIUtility.IconContent("ViewToolOrbit").image);
            config.m_TightPacking = EditorGUILayout.Toggle(tightPackingContent, config.m_TightPacking);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private static int DrawIntPopup(GUIContent label, int selectedValue, string[] displayedOptions,
            int[] optionValues)
        {
            using var horizontalScope = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.PrefixLabel(label);
            return EditorGUILayout.IntPopup(selectedValue, displayedOptions, optionValues);
        }


        private void DrawAdvancedSettings(AtlasConfiguration config)
        {
            EditorGUILayout.BeginVertical("box");
            var labelGUIContent = new GUIContent(" 高级设置", EditorGUIUtility.IconContent("ToolHandleGlobal").image);
            GUILayout.Label(labelGUIContent, EditorStyles.boldLabel, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            var autoGenerateContent = new GUIContent(" 自动生成", EditorGUIUtility.IconContent("PlayButton").image);
            config.m_AutoGenerate = EditorGUILayout.Toggle(autoGenerateContent, config.m_AutoGenerate);
            var enableLoggingContent =
                new GUIContent(" 启用日志", EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow").image);
            config.m_EnableLogging = EditorGUILayout.Toggle(enableLoggingContent, config.m_EnableLogging);
            var enableV2Content = new GUIContent(" 启用V2打包", EditorGUIUtility.IconContent("CollabNew").image);
            config.m_EnableV2 = EditorGUILayout.Toggle(enableV2Content, config.m_EnableV2);
            EditorGUILayout.BeginHorizontal();
            _showExcludeKeywords = EditorGUILayout.BeginFoldoutHeaderGroup(_showExcludeKeywords, "排除关键词");
            // GUILayout.Label("", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            if (_showExcludeKeywords)
            {
                GUILayout.Label("数量:", GUILayout.ExpandWidth(false));
                int newSize = EditorGUILayout.IntField(config.m_ExcludeKeywords.Length, GUILayout.Width(40));
                newSize = Mathf.Max(0, newSize);
                if (newSize != config.m_ExcludeKeywords.Length)
                {
                    Array.Resize(ref config.m_ExcludeKeywords, newSize);
                }

                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), GUILayout.Width(25),
                        GUILayout.Height(20)))
                {
                    Array.Resize(ref config.m_ExcludeKeywords, config.m_ExcludeKeywords.Length + 1);
                }

                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus"), GUILayout.Width(25),
                        GUILayout.Height(20)) && config.m_ExcludeKeywords.Length > 0)
                {
                    Array.Resize(ref config.m_ExcludeKeywords, config.m_ExcludeKeywords.Length - 1);
                }
            }

            EditorGUILayout.EndHorizontal();
            if (_showExcludeKeywords)
            {
                EditorGUILayout.BeginVertical("box");
                for (int i = 0; i < config.m_ExcludeKeywords.Length; i++)
                {
                    var keywordsContent =
                        new GUIContent($" 关键词 [{i}]", EditorGUIUtility.IconContent("FilterByLabel").image);
                    config.m_ExcludeKeywords[i] = EditorGUILayout.TextField(keywordsContent, config.m_ExcludeKeywords[i]);
                }

                GUILayout.Space(2);
                if (GUILayout.Button(new GUIContent(" 清空", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                        GUILayout.Height(25)))
                {
                    config.m_ExcludeKeywords = Array.Empty<string>();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawActionButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Color originalColor = GUI.color;
                GUI.color = Color.yellow;
                if (GUILayout.Button(new GUIContent(" 立即重新生成", EditorGUIUtility.IconContent("Refresh").image),
                        GUILayout.ExpandWidth(true), GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("确认删除", "此操作将会立即删除相关路径下的所有图集资源，并重新生成，确定继续吗？", "删除", "取消"))
                    {
                        EditorSpriteSaveInfo.ForceGenerateAll(true);
                    }
                }

                GUI.color = originalColor;

                if (GUILayout.Button(new GUIContent("重新生成有变动的图集数据", EditorGUIUtility.IconContent("Refresh").image), GUILayout.ExpandWidth(true), GUILayout.Height(30)))
                {
                    EditorSpriteSaveInfo.ForceGenerateAll();
                }

                if (GUILayout.Button(new GUIContent(" 清空缓存", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                        GUILayout.ExpandWidth(true), GUILayout.Height(30)))
                {
                    EditorSpriteSaveInfo.ClearCache();
                }
            }
        }
    }
}
#endif