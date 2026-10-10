using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 编辑器用。
    /// </summary>
    public static class AssetInfoHelper
    {
        public enum AssetCheckResult
        {
            /// <summary>路径正确但是 GUID 错误。</summary>
            FailGuidNotFound = -4,
            /// <summary>GUID 正确但是路径错误。</summary>
            FailPathNotFound = -3,
            FailAssetNotExist = -2,
            Fail = -1,
            Pass = 0,
            PassResource = 1,
            PassAssetBundle = 2,
            PassAssetPackage = 3,
        }
        
        /// <summary>
        /// 根据 GUID 检查资源是否合法。
        /// </summary>
        public static AssetCheckResult CheckAssetGuidAndPath<T>(string guid, string path) where T : UObject
        {
            if (string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(path))
            {
                return AssetCheckResult.Fail;
            }
            
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);

            string newPath = AssetDatabase.GUIDToAssetPath(guid);
            if (asset == null)
            {
                asset = AssetDatabase.LoadAssetAtPath<T>(newPath);
            }

            if (asset == null)
            {
                return AssetCheckResult.FailAssetNotExist;
            }

            if (newPath != path)
            {
                return string.IsNullOrEmpty(newPath) ?
                    AssetCheckResult.FailGuidNotFound : AssetCheckResult.FailPathNotFound;
            }

            // 检测资源是否位于 Resources 中
            if (newPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AssetCheckResult.PassResource;
            }

            // 检测资源是否位于 AssetBundle 中
            if (!string.IsNullOrEmpty(AssetDatabase.GetImplicitAssetBundleName(newPath)))
            {
                return AssetCheckResult.PassAssetBundle;
            }

            // 检测资源是否在打包路径
            if (newPath.IndexOf("/AssetRaw/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AssetCheckResult.PassAssetPackage;
            }

            return AssetCheckResult.Pass;
        }
        
        public static string GetResultCodeInfo(AssetCheckResult resultCode)
        {
            return resultCode switch
            {
                AssetCheckResult.FailGuidNotFound => "根据路径更新 GUID",
                AssetCheckResult.FailPathNotFound => "根据 GUID 更新路径",
                AssetCheckResult.FailAssetNotExist => "资源不存在！",
                AssetCheckResult.Fail => "未配置",
                AssetCheckResult.Pass => "资源必须位于 Resources 或 Asset Bundle 中!",
                AssetCheckResult.PassResource => "Resources",
                AssetCheckResult.PassAssetBundle => "AssetBundle",
                AssetCheckResult.PassAssetPackage => "AssetPackage",
                _ => ""
            };
        }
        
        public static string GenerateTextureInfo(Sprite sprite)
        {
            string textureInfo;

            if (sprite == null)
            {
                textureInfo = string.Empty;
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(sprite);

                string simpleName = Path.GetFileNameWithoutExtension(path);
                if (simpleName != sprite.name)
                {
                    textureInfo = sprite.name + ";" +
                                  sprite.textureRect.x + ";" + sprite.textureRect.y + ";" + sprite.textureRect.width +
                                  ";" + sprite.textureRect.height + ";" +
                                  sprite.pivot.x + ";" + sprite.pivot.y;
                }
                else
                {
                    textureInfo = string.Empty;
                }
            }

            return textureInfo;
        }

        public static Sprite GetSpriteFromImageInfo(string path, string textureInfo)
        {
            Sprite sprite;
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            
            if (!string.IsNullOrEmpty(textureInfo))
            {
                string[] parts = textureInfo.Split(';');
                try
                {
                    sprite = source.First(s => s.name == parts[0]);
                }
                catch
                {
                    // 没有则获取第一个
                    sprite = source[0];
                }
            }
            else
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return sprite;
        }

        public static Texture GetTextureFromImageInfo(string path, string textureInfo)
        {
            Texture texture;
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture>().ToArray();

            if (!string.IsNullOrEmpty(textureInfo))
            {
                string[] parts = textureInfo.Split(';');
                try
                {
                    texture = source.First(t => t.name == parts[0]);
                }
                catch
                {
                    // 没有则获取第一个
                    texture = source[0];
                }
            }
            else
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            }

            return texture;
        }

        /// <summary>
        /// 根据 guid 和 path 的值获取属性的高度。
        /// </summary>
        /// <param name="initialLines">初始属性个数。</param>
        public static float GetAssetInfoHeight<T>(string guid, string path, int initialLines = 1)
            where T : UObject
        {
            // 默认显示 精灵选择绘制框 和 packageType 2行
            int lines = initialLines;
            
            // 获取必要的属性值
            string newPath = AssetDatabase.GUIDToAssetPath(guid);

            // 计算文本高度
            float textWidth = EditorGUIUtility.currentViewWidth - 60; // unity 启用 wordWrap 时，默认整个单词自动换行的边距，用于计算行数
            float pathTextHeight = 0;
            float resultCodeTextHeight = 0f;
            
            if (
#if UNITY_6000_0_OR_NEWER
                AssetDatabase.AssetPathExists(newPath)
#else
                AssetDatabase.LoadAssetAtPath<UObject>(newPath) != null
#endif
                )
            {
                pathTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(newPath), textWidth);

                var resultCode = CheckAssetGuidAndPath<T>(guid, path);
                if (resultCode == AssetCheckResult.Pass)
                {
                    resultCodeTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(GetResultCodeInfo(resultCode)), textWidth);
                }
            }
            
            // Debug.Log($"guid {guid} {pathTextHeight} {resultCodeTextHeight}");
            // 总高度
            return (EditorGUIUtility.singleLineHeight + 2) * lines + pathTextHeight + resultCodeTextHeight;
        }

        /// <summary>
        /// 绘制资源信息。
        /// </summary>
        public static void DrawBaseAssetInfo<T>(ref Rect position, SerializedProperty property, string guidProperty, string pathProperty)
            where T : UObject
        {
            position.height = EditorGUIUtility.singleLineHeight;
           
            EditorGUI.BeginProperty(position, new GUIContent($"{typeof(T).Name} Info"), property);
  
            #region 检查资源是否已移动 [CHECK ASSET MOVED]
            
            string guid = property.FindPropertyRelative(guidProperty).stringValue;
            string path = property.FindPropertyRelative(pathProperty).stringValue;
            
            var resultCode = CheckAssetGuidAndPath<T>(guid, path);
            if (resultCode == AssetCheckResult.FailPathNotFound && !string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                property.FindPropertyRelative(pathProperty).stringValue = path;
                Debug.Log($"{property.displayName} 的 {typeof(T).Name} 位置已移动到 {path}");  
            }

            if (resultCode == AssetCheckResult.FailGuidNotFound)
            {
                guid = AssetDatabase.AssetPathToGUID(path);
                property.FindPropertyRelative(guidProperty).stringValue = guid;
                Debug.Log($"{property.displayName} 的 {typeof(T).Name} GUID 已修改为 {guid}");
            }
            
            #endregion
            
            // 根据保存的 GUID 加载资源
            EditorGUI.BeginChangeCheck();
            UObject target = EditorGUI.ObjectField(position, $"{property.displayName}",
                AssetDatabase.LoadAssetAtPath<T>(path), typeof(T), false);

            var newPath = AssetDatabase.GetAssetPath(target);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);
            if (EditorGUI.EndChangeCheck())
            {
                property.FindPropertyRelative(guidProperty).stringValue = newGuid;
                property.FindPropertyRelative(pathProperty).stringValue = newPath;
                Debug.Log($"更改 {property.displayName} 的 {typeof(T).Name} 为 {newPath}");
            }
            
            // -------------------- 绘制额外信息 ----------------------------
            if (target != null)
            {
                var checkAsset = CheckAssetGuidAndPath<T>(newGuid, newPath);
                string resultCodeInfo = GetResultCodeInfo(checkAsset);
                string pathInfo = checkAsset > AssetCheckResult.Pass
                    ? $"{resultCodeInfo}：{newPath}"
                    : $"Path：{newPath}"; 
                
                // 显示路径
                if (!string.IsNullOrEmpty(newPath))
                {
                    position.y += EditorGUIUtility.singleLineHeight + 2;
                    float pathTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(pathInfo), position.width);
    
                    Rect pathRect = new Rect(position.x, position.y, position.width, pathTextHeight);
                    GUI.Label(pathRect, pathInfo, CustomStyles.InfoTextStyle);
                    position.y += pathTextHeight + 2;
                }
                
                // 显示提示
                if (checkAsset == AssetCheckResult.Pass)
                {
                    float resultCodeTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(resultCodeInfo), position.width);
                    Rect resultRect = new Rect(position.x, position.y, position.width, resultCodeTextHeight);
                    GUI.Label(resultRect, resultCodeInfo, CustomStyles.ErrorTextStyle);
                    position.y += resultCodeTextHeight + 2;
                }
            }
            else
            {
                position.y += EditorGUIUtility.singleLineHeight + 2;
            }
            
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 根据 guid 的值获取弱引用属性的高度（只有 GUID、无缓存路径字段的 <c>AssetReference</c> 形）。
        /// </summary>
        /// <param name="guid">资源 GUID。</param>
        /// <param name="initialLines">初始属性个数。</param>
        public static float GetGuidAssetInfoHeight<T>(string guid, int initialLines = 1)
            where T : UObject
        {
            int lines = initialLines;

            string path = AssetDatabase.GUIDToAssetPath(guid);

            float textWidth = EditorGUIUtility.currentViewWidth - 60; // unity 启用 wordWrap 时，默认整个单词自动换行的边距，用于计算行数
            float pathTextHeight = 0;
            float resultCodeTextHeight = 0f;

            if (
#if UNITY_6000_0_OR_NEWER
                AssetDatabase.AssetPathExists(path)
#else
                AssetDatabase.LoadAssetAtPath<UObject>(path) != null
#endif
               )
            {
                pathTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(path), textWidth);

                var resultCode = CheckAssetGuidAndPath<T>(guid, path);
                if (resultCode == AssetCheckResult.Pass)
                {
                    resultCodeTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(GetResultCodeInfo(resultCode)), textWidth);
                }
            }
            else if (!string.IsNullOrEmpty(guid))
            {
                // GUID 还挂着但资源已不存在：保留一行报错空间
                resultCodeTextHeight = CustomStyles.InfoTextStyle.CalcHeight(
                    new GUIContent(GetResultCodeInfo(AssetCheckResult.FailAssetNotExist)), textWidth);
            }

            return (EditorGUIUtility.singleLineHeight + 2) * lines + pathTextHeight + resultCodeTextHeight;
        }

        /// <summary>
        /// 绘制弱引用的对象字段行（选中即写回 GUID）。
        /// </summary>
        /// <returns>选中的资源对象；未选中为 <c>null</c>。</returns>
        public static T DrawGuidAssetField<T>(ref Rect position, SerializedProperty property, string guidProperty)
            where T : UObject
        {
            position.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.BeginProperty(position, new GUIContent($"{typeof(T).Name} Reference"), property);

            string path = AssetDatabase.GUIDToAssetPath(property.FindPropertyRelative(guidProperty).stringValue);

            // 根据保存的 GUID 加载资源
            EditorGUI.BeginChangeCheck();
            T target = EditorGUI.ObjectField(position, $"{property.displayName}",
                AssetDatabase.LoadAssetAtPath<T>(path), typeof(T), false) as T;

            if (EditorGUI.EndChangeCheck())
            {
                property.FindPropertyRelative(guidProperty).stringValue =
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));
            }

            position.y += EditorGUIUtility.singleLineHeight + 2;

            EditorGUI.EndProperty();
            return target;
        }

        /// <summary>
        /// 绘制弱引用字段行之后的附加信息：路径与打包归属提示，GUID 悬空（资产已不存在）时报错。
        /// </summary>
        public static void DrawGuidAssetHints<T>(ref Rect position, UObject target, string guid)
            where T : UObject
        {
            if (target != null)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var checkAsset = CheckAssetGuidAndPath<T>(guid, path);

                if (!string.IsNullOrEmpty(path))
                {
                    string resultCodeInfo = GetResultCodeInfo(checkAsset);
                    string pathInfo = checkAsset > AssetCheckResult.Pass
                        ? $"{resultCodeInfo}：{path}"
                        : $"Path：{path}";

                    float pathTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(pathInfo), position.width);
                    GUI.Label(new Rect(position.x, position.y, position.width, pathTextHeight), pathInfo, CustomStyles.InfoTextStyle);
                    position.y += pathTextHeight + 2;

                    if (checkAsset == AssetCheckResult.Pass)
                    {
                        float resultCodeTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(resultCodeInfo), position.width);
                        GUI.Label(new Rect(position.x, position.y, position.width, resultCodeTextHeight), resultCodeInfo, CustomStyles.ErrorTextStyle);
                        position.y += resultCodeTextHeight + 2;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(guid))
            {
                string errorInfo = GetResultCodeInfo(AssetCheckResult.FailAssetNotExist);
                float errorTextHeight = CustomStyles.InfoTextStyle.CalcHeight(new GUIContent(errorInfo), position.width);
                GUI.Label(new Rect(position.x, position.y, position.width, errorTextHeight), errorInfo, CustomStyles.ErrorTextStyle);
                position.y += errorTextHeight + 2;
            }
        }

        /// <summary>
        /// 绘制资源信息。
        /// </summary>
        public static void DrawBaseAssetInfo<T>(SerializedProperty property, string guidProperty, string pathProperty, string title = "")
            where T : UObject
        {
           
            #region 检查资源是否已移动 [CHECK ASSET MOVED]
            
            string path = property.FindPropertyRelative(pathProperty).stringValue;
            string guid = property.FindPropertyRelative(guidProperty).stringValue;
    
            var resultCode = CheckAssetGuidAndPath<T>(guid, path);
            if (resultCode == AssetCheckResult.FailPathNotFound && !string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                property.FindPropertyRelative(pathProperty).stringValue = path;
                Debug.Log($"{property.displayName} 的 {typeof(T).Name} 位置已移动到 {path}");  
            }

            if (resultCode == AssetCheckResult.FailGuidNotFound)
            {
                guid = AssetDatabase.AssetPathToGUID(path);
                property.FindPropertyRelative(guidProperty).stringValue = guid;
                Debug.Log($"{property.displayName} 的 {typeof(T).Name} GUID 已修改为 {guid}");
            }
            
            #endregion

            if (string.IsNullOrEmpty(title))
            {
                title = typeof(T).Name;
            }
            
            // 根据保存的 GUID 加载资源
            EditorGUI.BeginChangeCheck();
            UObject target = EditorGUILayout.ObjectField(title,
                AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)),
                typeof(T), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            
            var newPath = AssetDatabase.GetAssetPath(target);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);
            if (EditorGUI.EndChangeCheck())
            {
                property.FindPropertyRelative(guidProperty).stringValue = newGuid;
                property.FindPropertyRelative(pathProperty).stringValue = newPath;
                Debug.Log($"更改 {property.displayName} 的 {typeof(T).Name} 为 {newPath}");
            }
            
            // -------------------- 绘制额外信息 ----------------------------
            if (target != null)
            {
                var checkAsset = CheckAssetGuidAndPath<T>(newGuid, newPath);
                string resultCodeInfo = GetResultCodeInfo(checkAsset);
                string pathInfo = checkAsset > AssetCheckResult.Pass
                    ? $"{resultCodeInfo}：{newPath}"
                    : $"Path：{newPath}"; 
                
                // 显示路径
                if (!string.IsNullOrEmpty(newPath))
                {
                    EditorGUILayout.LabelField("", pathInfo, CustomStyles.InfoTextStyle);
                }
                
                // 显示提示
                if (checkAsset == AssetCheckResult.Pass)
                {
                    EditorGUILayout.LabelField("", resultCodeInfo, CustomStyles.ErrorTextStyle);
                }
            }
        }

        /// <summary>
        /// 绘制资源信息。
        /// </summary>
        public static void DrawBaseAssetInfo<T>(SerializedProperty guidProperty, SerializedProperty pathProperty, string title = "")
            where T : UObject
        {

            #region 检查资源是否已移动 [CHECK ASSET MOVED]

            string path = pathProperty.stringValue;
            string guid = guidProperty.stringValue;

            var resultCode = CheckAssetGuidAndPath<T>(guid, path);
            if (resultCode == AssetCheckResult.FailPathNotFound && !string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                pathProperty.stringValue = path;
                Debug.Log($"{typeof(T).Name} 位置已移动到 {path}");
            }

            if (resultCode == AssetCheckResult.FailGuidNotFound)
            {
                guid = AssetDatabase.AssetPathToGUID(path);
                guidProperty.stringValue = guid;
                Debug.Log($"{typeof(T).Name} GUID 已修改为 {guid}");
            }

            #endregion

            if (string.IsNullOrEmpty(title))
            {
                title = typeof(T).Name;
            }

            // 根据保存的 GUID 加载资源
            EditorGUI.BeginChangeCheck();
            UObject target = EditorGUILayout.ObjectField(title,
                AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)),
                typeof(T), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            var newPath = AssetDatabase.GetAssetPath(target);
            var newGuid = AssetDatabase.AssetPathToGUID(newPath);
            if (EditorGUI.EndChangeCheck())
            {
                guidProperty.stringValue = newGuid;
                pathProperty.stringValue = newPath;
                Debug.Log($"更改 {typeof(T).Name} 为 {newPath}");
            }

            // -------------------- 绘制额外信息 ----------------------------
            if (target != null)
            {
                var checkAsset = CheckAssetGuidAndPath<T>(newGuid, newPath);
                string resultCodeInfo = GetResultCodeInfo(checkAsset);
                string pathInfo = checkAsset > AssetCheckResult.Pass
                    ? $"{resultCodeInfo}：{newPath}"
                    : $"Path：{newPath}";

                // 显示路径
                if (!string.IsNullOrEmpty(newPath))
                {
                    EditorGUILayout.LabelField("", pathInfo, CustomStyles.InfoTextStyle);
                }

                // 显示提示
                if (checkAsset == AssetCheckResult.Pass)
                {
                    EditorGUILayout.LabelField("", resultCodeInfo, CustomStyles.ErrorTextStyle);
                }
            }
        }
    }
}