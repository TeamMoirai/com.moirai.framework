#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Moirai.Atropos.Editor
{
    [FilePath("ProjectSettings/AtlasConfiguration.asset", FilePathAttribute.Location.ProjectFolder)]
    public class AtlasConfiguration : EditorScriptableSingleton<AtlasConfiguration>
    {
        [Header("目录设置")]
        [Tooltip("生成的图集输出目录")]
        [SerializeField] internal string m_OutputAtlasDir = "Assets/AssetArt/Atlas";

        [Tooltip("需要生成图集的UI根目录")]
        [SerializeField] internal string[] m_SourceAtlasRootDir = new string[] { "Assets/AssetRaw/UIRaw/Atlas" };
        [Tooltip("以当前目录的子级生成子级图集")]
        [SerializeField] internal string[] m_RootChildAtlasDir = new string[] {  };
        [Tooltip("每张图都单独生成图集")]
        [SerializeField] internal string[] m_SingleAtlasDir = new string[] { "Assets/AssetRaw/UIRaw/Atlas/Background" };
        [Tooltip("不需要生成图集的UI目录")]
        [SerializeField] internal string[] m_ExcludeFolder = new string[] { "Assets/AssetRaw/UIRaw/Raw" };

        [Header("平台格式设置")]
        [SerializeField] internal TextureImporterFormat m_AndroidFormat = TextureImporterFormat.ASTC_6x6;

        [SerializeField] internal TextureImporterFormat m_IOSFormat = TextureImporterFormat.ASTC_5x5;
        // ReSharper disable once InconsistentNaming
        [SerializeField] internal TextureImporterFormat m_WEBGLFormat = TextureImporterFormat.ASTC_6x6;

        [Header("PackingSetting")]
        [SerializeField] internal int m_Padding = 2;

        [SerializeField] internal bool m_EnableRotation = true;
        [SerializeField] internal int m_BlockOffset = 1;
        [SerializeField] internal bool m_TightPacking = true;

        [Header("其他设置")]
        [Range(0, 100)]
        [SerializeField] internal int m_CompressionQuality = 50;

        [SerializeField] internal bool m_AutoGenerate = true;
        [SerializeField] internal bool m_EnableLogging = true;
        [SerializeField] internal bool m_EnableV2 = true;

        [Header("Sprite导入设置")]
        [SerializeField] internal bool m_CheckMipmaps = true;
        [SerializeField] internal bool m_EnableMipmaps = false;
        [SerializeField] internal TextureImporterCompression m_TextureCompression = TextureImporterCompression.Compressed;

        [Header("排除关键词")]
        [SerializeField] internal string[] m_ExcludeKeywords = { "_Delete", "_Temp" };
    }
}
#endif