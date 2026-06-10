using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YooAsset.Editor
{
    [DisplayName("收集音频（自定义）")]
    public class AudioFilter : IFilterRule
    {
        private static readonly HashSet<string> s_AudioFileExtensions = new HashSet<string>() { ".mp3", ".flac", ".ogg", ".wav" };

        public string FindAssetType => EAssetSearchType.AudioClip.ToString();

        public bool IsCollectAsset(FilterRuleData data)
        {
            return s_AudioFileExtensions.Contains(Path.GetExtension(data.AssetPath).ToLower());
        }
    }

    [DisplayName("收集着色器（自定义）")]
    public class ShaderFilter : IFilterRule
    {
        private static readonly HashSet<string> s_ShaderFileExtensions = new HashSet<string>() { ".shader", ".shadervariants", ".cginc" };

        public string FindAssetType => EAssetSearchType.All.ToString();

        public bool IsCollectAsset(FilterRuleData data)
        {
            return s_ShaderFileExtensions.Contains(Path.GetExtension(data.AssetPath).ToLower());
        }
    }

    [DisplayName("收集所有非精灵类型的资源（自定义）")]
    public class CollectAllWithoutSprite : IFilterRule
    {
        public string FindAssetType => EAssetSearchType.All.ToString();

        public bool IsCollectAsset(FilterRuleData data)
        {
            var mainAssetType = AssetDatabase.GetMainAssetTypeAtPath(data.AssetPath);
            if (mainAssetType == typeof(Texture2D))
            {
                var texImporter = AssetImporter.GetAtPath(data.AssetPath) as TextureImporter;
                if (texImporter != null && texImporter.textureType == TextureImporterType.Sprite)
                    return false;
                else
                    return true;
            }
            else
            {
                return true;
            }
        }
    }
}