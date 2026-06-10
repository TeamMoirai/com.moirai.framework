using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Moirai.Atropos.Input.Prompts;
using Sirenix.OdinInspector.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore;

namespace Moirai.Atropos.Editor.Input.Prompts
{
    [CustomEditor(typeof(GlyphCollection))]
    public class GlyphCollectionEditor : OdinEditor
    {
        private readonly HashSet<string> _usedSpriteSheetNames = new HashSet<string>();

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            if (GUILayout.Button("生成 TMP Sprite Assets"))
            {
	            bool confirm = EditorUtility.DisplayDialog("Confirm Generation",
		            "为每个 Device Prompt Asset 创建 TextMeshPro Sprite 资产，从而允许它们与 TextMeshPro 内联使用。此操作是不可逆的。",
		            "生成", "取消");
	            if (!confirm) return;
	            
                _usedSpriteSheetNames.Clear();
                
                // 获取要从中生成的集合
                GlyphCollection collection = target as GlyphCollection;
                if (collection == null)
                {
                    Debug.LogError("未设置 Glyph Collection");
                }
                else
                {
                    GenerateForCollection(collection);
                }
            }

            if (GUILayout.Button("获取所有支持的 InputDevice"))
            {
	            var controllers = new Dictionary<string, string>();
	            Type inputDevice = typeof(InputDevice);
	            Type[] types;
	            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
	            foreach (Assembly asm in assemblies)
	            {
		            types = asm.GetTypes();
		            foreach (Type type in types)
		            {
			            if (type.BaseType != null && inputDevice.IsAssignableFrom(type))
			            {
				            controllers.Add(ObjectNames.NicifyVariableName(type.Name), type.Name);
			            }
		            }
	            }
	            controllers = controllers.Keys.OrderBy(k => k).ToDictionary(k => k, k => controllers[k]);

	            StringBuilder content = new StringBuilder();
	            content.AppendLine($"现在可以将下面的输出复制到 {nameof(PromptGlyph)}.cs 的 DeviceNames 构造函数中");
	            content.AppendLine();
	            
	            foreach (var controller in controllers)
	            {
		            content.Append($"            {{ \"{controller.Key}\", \"{controller.Value}\" }},");
		            content.AppendLine();
	            }
	            Debug.Log(content);
            }
        }
        
	    private void GenerateForCollection(GlyphCollection collection)
		{
			// 获取所有按键图标字形
			List<PromptGlyph> glyphs = new List<PromptGlyph>();
			glyphs.AddRange(collection.PromptMaps.SelectMany(guidEntry => guidEntry.ActionGlyphs));
			glyphs.Add(collection.DisconnectGlyph);
			glyphs.Add(collection.NullGlyph);
			glyphs.Add(collection.UnboundGlyph);
			
			// 从所有字形中获取所有唯一的资源名称。
			HashSet<string> spriteAssetPaths = new HashSet<string>();
			foreach (string assetPath in from glyph in glyphs select glyph.Icon into sprite where sprite != null select AssetDatabase.GetAssetPath(sprite))
			{
				spriteAssetPaths.Add(assetPath);
			}
			
			string collectionPath = AssetDatabase.GetAssetPath(collection);
			int startIndex = "Assets/".Length; // 去除地址的前缀 Assets/
			int lastSlashIndex = collectionPath.LastIndexOf('/'); // 去除地址的文件名
			string trimmedPath = collectionPath.Substring(startIndex, lastSlashIndex - startIndex);
			
			string parentPath = $"{Application.dataPath}/{trimmedPath}/Resources/{TMP_Settings.defaultSpriteAssetPath}";
			// 删除旧的 SpriteAsset 文件夹
			if (Directory.Exists(parentPath))
			{
				Directory.Delete(parentPath, true);
			}
			
			Dictionary<string, SpriteSheetOutput> spriteSheetOutputs = new Dictionary<string, SpriteSheetOutput>();
			foreach (string spriteAssetPath in spriteAssetPaths)
			{
				Texture2D assetAtPath = AssetDatabase.LoadAssetAtPath<Texture2D>(spriteAssetPath);
				SpriteSheetOutput sheetOutput = GenerateSpriteAsset(assetAtPath, parentPath, collection);
				if (_usedSpriteSheetNames.Add(sheetOutput.spriteAssetName) == false)
				{
					Debug.LogError($"Generated multiple sprite sheets with name \"{sheetOutput.spriteAssetName}\". Multiple sprite sheets can't have the same name, please rename.");
				}
			
				spriteSheetOutputs.Add(sheetOutput.spriteAssetPath, sheetOutput);
			}
			
			foreach (PromptGlyph glyph in glyphs)
			{
				Sprite sprite = glyph.Icon;
				if (sprite == null)
				{
					continue;
				}
			
				string assetPath = AssetDatabase.GetAssetPath(sprite);
				if (spriteSheetOutputs.TryGetValue(assetPath, out SpriteSheetOutput spriteSheetOutput))
				{
					glyph.TextMeshSpriteSheetName = spriteSheetOutput.spriteAssetName;
				}
			}
			
			// 将资产设置为 dirty
			foreach (GlyphMap guidEntry in collection.PromptMaps)
			{
				EditorUtility.SetDirty(guidEntry);
			}
			
			EditorUtility.SetDirty(collection);
			AssetDatabase.Refresh();
		}

	    /// <summary>
	    /// 基于 <see cref="TMPro.EditorUtilities.TMP_SpriteAssetMenu"/>
	    /// </summary>
	    /// <param name="target"></param>
	    /// <param name="parentPath"></param>
	    /// <param name="collection"></param>
	    /// <returns></returns>
	    private static SpriteSheetOutput GenerateSpriteAsset(Texture2D target, string parentPath, GlyphCollection collection)
	    {
		    // Get the path to the selected asset.
		    string filePathWithName = AssetDatabase.GetAssetPath(target);
		    string spriteAssetName = $"{collection.Key}_{Path.GetFileNameWithoutExtension(filePathWithName)}";

		    int startIndex = Application.dataPath.Length;
		    string spriteAssetPath = parentPath.Substring(startIndex, parentPath.Length - startIndex);
		    
		    // Create new Sprite Asset
		    TMP_SpriteAsset spriteAsset = CreateInstance<TMP_SpriteAsset>();
		    Directory.CreateDirectory(parentPath);
		    AssetDatabase.CreateAsset(spriteAsset, $"Assets/{spriteAssetPath}{spriteAssetName}.asset");
		    SetProperty(spriteAsset, nameof(TMP_SpriteAsset.version), "1.1.0");

		    // Compute the hash code for the sprite asset.
		    spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);

		    List<TMP_SpriteGlyph> spriteGlyphTable = new List<TMP_SpriteGlyph>();
		    List<TMP_SpriteCharacter> spriteCharacterTable = new List<TMP_SpriteCharacter>();

		    // Assign new Sprite Sheet texture to the Sprite Asset.
		    spriteAsset.spriteSheet = target;
		    PopulateSpriteTables(target, ref spriteCharacterTable, ref spriteGlyphTable);
		    SetProperty(spriteAsset, nameof(TMP_SpriteAsset.spriteCharacterTable), spriteCharacterTable);
		    SetProperty(spriteAsset, nameof(TMP_SpriteAsset.spriteGlyphTable), spriteGlyphTable);

		    // Add new default material for sprite asset.
		    AddDefaultMaterial(spriteAsset);

		    // Update Lookup tables.
		    spriteAsset.UpdateLookupTables();
		    
		    // UpdateSpriteInfo
		    foreach (TMP_SpriteGlyph glyph in spriteAsset.spriteGlyphTable)
		    {
			    // 创建新Metrics结构体修改值
			    GlyphMetrics metrics = new GlyphMetrics(
				    width: glyph.metrics.width,
				    height: glyph.metrics.height,
				    bearingX: collection.m_OffsetX != 0 ? collection.m_OffsetX : glyph.metrics.horizontalBearingX,  // 替换目标OX值
				    bearingY: collection.m_OffsetY != 0 ? collection.m_OffsetY : glyph.metrics.horizontalBearingY,  // 替换目标OY值
				    advance: collection.m_Advance != 0 ? collection.m_Advance : glyph.metrics.horizontalAdvance   // 替换目标ADV值
			    );
        
			    glyph.metrics = metrics;
			    glyph.scale = (collection.m_ScaleFactor > 0f && collection.m_ScaleFactor != 1f) ? collection.m_ScaleFactor : 1f; // 替换目标SF值
		    }
		    // 强制更新所有使用该sprite的文本组件
		    TMPro_EventManager.ON_SPRITE_ASSET_PROPERTY_CHANGED(true, spriteAsset);
		    
		    // Set dirty
		    EditorUtility.SetDirty(spriteAsset);
		    AssetDatabase.SaveAssets();
		    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(spriteAsset));
		    
		    return new SpriteSheetOutput(filePathWithName, spriteAssetName);
	    }
	    
	    private static void SetProperty(TMP_SpriteAsset spriteAsset, string propertyName, object value)
	    {
		    PropertyInfo propertyInfo = typeof(TMP_SpriteAsset).GetProperty(propertyName);
		    if (propertyInfo != null)
		    {
			    propertyInfo.SetValue(spriteAsset, value);
		    }
	    }
	    
	    /// <summary>
	    /// 基于 <see cref="TMPro.EditorUtilities.TMP_SpriteAssetMenu"/>
	    /// </summary>
	    /// <param name="source"></param>
	    /// <param name="spriteCharacterTable"></param>
	    /// <param name="spriteGlyphTable"></param>
	    private static void PopulateSpriteTables(Texture source, ref List<TMP_SpriteCharacter> spriteCharacterTable, ref List<TMP_SpriteGlyph> spriteGlyphTable)
	    {
		    // Debug.Log("Creating new Sprite Asset.");
		    
		    string filePath = AssetDatabase.GetAssetPath(source);

		    // Get all the Sprites sorted by Index
		    Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(filePath).Select(x => x as Sprite).Where(x => x != null).OrderByDescending(x => x.rect.y).ThenBy(x => x.rect.x).ToArray();

		    for (int i = 0; i < sprites.Length; i++)
		    {
			    Sprite sprite = sprites[i];
			    TMP_SpriteGlyph spriteGlyph = new TMP_SpriteGlyph
			    {
				    index = (uint)i,
				    metrics = new GlyphMetrics(sprite.rect.width, sprite.rect.height, 0, sprite.rect.height * 0.9f, sprite.rect.width),
				    glyphRect = new GlyphRect(sprite.rect),
				    scale = 1.0f,
				    sprite = sprite
			    };
			    
			    spriteGlyphTable.Add(spriteGlyph);

			    TMP_SpriteCharacter spriteCharacter = new TMP_SpriteCharacter(0xFFFE, spriteGlyph) { name = sprite.name, scale = 1.0f };

			    // Special handling for .notdef sprite name.
			    string fileNameToLowerInvariant = sprite.name.ToLowerInvariant();
			    if (fileNameToLowerInvariant == ".notdef" || fileNameToLowerInvariant == "notdef")
			    {
				    spriteCharacter.unicode = 0;
				    spriteCharacter.name = fileNameToLowerInvariant;
			    }
			    else
			    {
				    if (!string.IsNullOrEmpty(sprite.name) && sprite.name.Length > 2 && sprite.name[0] == '0' && (sprite.name[1] == 'x' || sprite.name[1] == 'X'))
				    {
					    spriteCharacter.unicode = (uint)TMP_TextUtilities.StringHexToInt(sprite.name.Remove(0, 2));
				    }
				    spriteCharacter.name = sprite.name;
			    }
			    
			    spriteCharacterTable.Add(spriteCharacter);
		    }
	    }

	    /// <summary>
	    /// 基于 <see cref="TMPro.EditorUtilities.TMP_SpriteAssetMenu"/>
	    /// </summary>
	    /// <param name="spriteAsset"></param>
	    private static void AddDefaultMaterial(TMP_SpriteAsset spriteAsset)
	    {
		    Shader shader = Shader.Find("TextMeshPro/Sprite");
		    Material material = new Material(shader);
		    material.SetTexture(ShaderUtilities.ID_MainTex, spriteAsset.spriteSheet);

		    spriteAsset.material = material;
		    // material.hideFlags = HideFlags.HideInHierarchy;
		    material.name = spriteAsset.name + " Material";
		    AssetDatabase.AddObjectToAsset(material, spriteAsset);
	    }
	    
	    private class SpriteSheetOutput
	    {
		    public readonly string spriteAssetPath;
		    public readonly string spriteAssetName;

		    public SpriteSheetOutput(string spriteAssetPath, string spriteAssetName)
		    {
			    this.spriteAssetPath = spriteAssetPath;
			    this.spriteAssetName = spriteAssetName;
		    }
	    }
	}
}