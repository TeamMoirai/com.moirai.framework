using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 示例编辑器工具：批量调整项目中 TMP_SpriteAsset 的字形度量与缩放。
/// </summary>
public static class SetTextSpriteScale
{
    /// <summary>
    /// 菜单入口：查找 Packages 目录下所有 TMP_SpriteAsset，并逐个调整其字形表。
    /// </summary>
    [MenuItem("Samples/Prompts/Set Text Sprite Scale")]
    public static void SetTextSpriteScales()
    {
        var allTextSprites = FindAllScriptableObjectsOfType<TMP_SpriteAsset>("","Packages");
        Debug.Log($"Found assets - length {allTextSprites.Count}");
        foreach (var textSprite in allTextSprites)
        {
            if (textSprite != null)
            {
                Debug.Log($"Found {textSprite.name}");
                ProcessSprites(textSprite);
            }
            // textSprite.spriteAssetScale = 1;
            
        }
    }

    /// <summary>
    /// 将每个字形的 horizontalBearingY 统一调整为 80、缩放调整为 1.2，并将资产标记为脏以便保存。
    /// </summary>
    private static void ProcessSprites(TMP_SpriteAsset textSprite)
    {
        foreach (var sprite in textSprite.spriteGlyphTable)
        {
            var metrics = sprite.metrics;
            metrics.horizontalBearingY = 80;
            sprite.metrics = metrics;
            sprite.scale = 1.2f;
        }
        EditorUtility.SetDirty(textSprite);
    }

    /// <summary>
    /// 查找指定目录下所有指定类型的 ScriptableObject 资产。
    /// </summary>
    /// <param name="filter">资源过滤关键字，空字符串表示不过滤。</param>
    /// <param name="folder">搜索目录，默认 <c>"Assets"</c>。</param>
    /// <returns>匹配到的资产列表。</returns>
    public static List<T> FindAllScriptableObjectsOfType<T>(string filter, string folder = "Assets")
        where T : ScriptableObject
    {
        return AssetDatabase.FindAssets(filter, new[] { folder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
            .ToList();
    }

    
}
