using Moirai.Atropos;
using Sirenix.OdinInspector.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// <see cref="SpriteAssetIndex"/> 的 Odin 绘制器：图集弱引用一行（复刻 <see cref="AssetReferenceDrawer"/> 默认实现），
    /// 下标用按图集精灵数钳制的滑动条，命中精灵预览紧贴字段左侧（对齐旧版 CurrencyPane 口径）。
    /// </summary>
    /// <remarks>
    /// <see cref="DrawerPriority"/> 取 (1, 0, 0)：先于默认优先级的特性 Drawer，
    /// 字段上的 [BoxGroup]/[InlineProperty]/[HideLabel]（InfoObject 同款）与本 Drawer 共存——
    /// 否则 InlineProperty 特性 Drawer 拦截后直接画子字段，本 Drawer 失效；BoxGroup 是结构性分组、不占链位，照常渲染。
    /// 序列化定位：Odin 4 分组特性（BoxGroup 等）下 UnityPropertyPath 带前导点、深层路径经 GetUnityPropertyForPath
    /// 会落到错误的包装对象——故沿父链重组 Unity 相对路径（跳过「#」分组段）后直接 FindProperty，
    /// 内部字段一律 FindPropertyRelative 相对寻址，读数与写回都不经 Odin 值缓存（外部 Apply 后缓存不刷新，
    /// 预览读缓存会停在旧图集）；字段缺失或纯 Odin 宿主时回退默认绘制，回退判定必须先于任何绘制，避免画半截再重复；
    /// 精灵数与预览取像：精灵表（spriteInfoList）非空走精灵表，否则退回字形表（spriteGlyphTable，旧版 CurrencyPane 同款回退——
    /// 本项目部分图集只填了字形表）；两者皆空时下标退回普通整数字段、不画预览。
    /// </remarks>
    [DrawerPriority(1, 0, 0)]
    public class SpriteAssetIndexDrawer : OdinValueDrawer<SpriteAssetIndex>
    {
        private const string ASSET_PROPERTY = "m_SpriteAsset";
        private const string INDEX_PROPERTY = "m_SpriteIndex";
        private const string GUID_PROPERTY = "m_GUID";
        private const string PACKAGE_PROPERTY = "m_PackageName";
        private const float PREVIEW_SIZE = 48f;

        private static readonly GUIContent s_PackageContent =
            new GUIContent("Package", "资源包名称，留空使用后端配置的默认资源包");

        protected override void DrawPropertyLayout(GUIContent label)
        {
            InspectorProperty assetProperty = Property.Children[ASSET_PROPERTY];
            InspectorProperty indexProperty = Property.Children[INDEX_PROPERTY];
            SerializedProperty rootProperty = ResolveRootProperty();
            SerializedProperty indexProp = rootProperty?.FindPropertyRelative(INDEX_PROPERTY);
            SerializedProperty assetProp = rootProperty?.FindPropertyRelative(ASSET_PROPERTY);
            SerializedProperty guidProp = assetProp?.FindPropertyRelative(GUID_PROPERTY);
            if (ValueEntry.SmartValue == null || assetProperty == null || indexProperty == null
                || indexProp == null || assetProp == null || guidProp == null)
            {
                CallNextDrawer(label);
                return;
            }

            // 图集读数与对象字段同源：一律走 Unity 序列化数据（Odin 的 SmartValue 缓存不随外部 Apply 刷新，
            // 预览若读缓存会停在旧图集）
            string guid = guidProp.stringValue;
            string assetPath = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            TMP_SpriteAsset spriteAsset = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetPath);
            int spriteCount = GetSpriteCount(spriteAsset);
            Sprite previewSprite = GetSpriteAt(spriteAsset, indexProp.intValue);

            // 两行标签统一用同款自绘（GUILayout.Label + EditorStyles.label，含同款边距），字段名严格左对齐；
            // 列宽取两行标签实宽较大者（复算 AssetReferenceDrawer 的 CalcSize + 2 公式），控件列随之对齐
            GUIContent assetLabel = BuildLabel(assetProperty.Label ?? GUIContent.none, assetPath);
            GUIContent indexLabel = indexProperty.Label ?? GUIContent.none;
            float labelWidth = Mathf.Max(
                EditorStyles.label.CalcSize(assetLabel).x,
                EditorStyles.label.CalcSize(indexLabel).x) + 2f;

            // 左列预览 + 右侧字段两行：预览紧贴字段左侧；图集未配置或精灵缺失时不占列
            EditorGUILayout.BeginHorizontal();
            Rect previewRect = Rect.zero;
            if (previewSprite != null)
            {
                previewRect = GUILayoutUtility.GetRect(PREVIEW_SIZE, PREVIEW_SIZE, GUILayout.Width(PREVIEW_SIZE),
                    GUILayout.Height(PREVIEW_SIZE));
            }

            EditorGUILayout.BeginVertical();

            // 行一：图集弱引用（复刻 AssetReferenceDrawer 默认一行实现：自绘标签 + 对象字段 + Package 下拉 + 悬空报错）
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(assetLabel, GUILayout.Width(labelWidth));
            Rect fieldRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true));
            UObject current = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath(assetPath, typeof(TMP_SpriteAsset));
            EditorGUI.BeginChangeCheck();
            UObject selected = EditorGUI.ObjectField(fieldRect, current, typeof(TMP_SpriteAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                guidProp.stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected));
                WriteThrough(assetProp);
            }

            if (AssetPackageBridge.GetPackageNames() != null)
            {
                DrawPackagePopup(assetProp.FindPropertyRelative(PACKAGE_PROPERTY));
            }

            EditorGUILayout.EndHorizontal();

            // GUID 悬空：资产已不存在时行下报错（与 AssetReferenceDrawer 同款）
            if (current == null && !string.IsNullOrEmpty(guid))
            {
                EditorGUILayout.LabelField(
                    AssetInfoHelper.GetResultCodeInfo(AssetInfoHelper.AssetCheckResult.FailAssetNotExist),
                    CustomStyles.ErrorTextStyle);
            }

            // 行二：下标滑动条，按图集精灵数钳制；图集未配置或精灵/字形表皆空时退回普通整数字段
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(indexLabel, GUILayout.Width(labelWidth));
            Rect sliderRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true));
            EditorGUI.BeginChangeCheck();
            int value = spriteCount > 0
                ? EditorGUI.IntSlider(sliderRect, indexProp.intValue, 0, spriteCount - 1)
                : EditorGUI.IntField(sliderRect, indexProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                indexProp.intValue = value;
                WriteThrough(indexProp);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (previewSprite != null)
            {
                // 精灵与两行字段块垂直居中对齐：64 框占位仍从组顶起，绘制时把框平移到行块中心（同帧 fieldRect/sliderRect 即行块边界）
                float blockCenter = (fieldRect.yMin + sliderRect.yMax) * 0.5f;
                var centeredFrame = new Rect(previewRect.x, blockCenter - PREVIEW_SIZE * 0.5f, PREVIEW_SIZE, PREVIEW_SIZE);
                DrawSprite(centeredFrame, previewSprite);
            }
        }

        /// <summary>
        /// 解析本 member 的 Unity 序列化属性：沿父链重组相对路径（跳过「#」分组段与根节点）后直接 FindProperty。
        /// 不走 Odin 的 UnityPropertyPath / GetUnityPropertyForPath——分组下前者带前导点、后者深层路径会落到错误的包装对象。
        /// </summary>
        private SerializedProperty ResolveRootProperty()
        {
            SerializedObject serializedObject = Property.Tree.UnitySerializedObject;
            if (serializedObject == null)
            {
                return null;
            }

            var segments = new System.Collections.Generic.List<string>();
            for (var current = Property; current != null; current = current.Parent)
            {
                if (current.Parent == null || string.IsNullOrEmpty(current.Name) || current.Name.StartsWith("#"))
                {
                    continue;
                }

                segments.Add(current.Name);
            }

            if (segments.Count == 0)
            {
                return null;
            }

            segments.Reverse();
            return serializedObject.FindProperty(string.Join(".", segments.ToArray()));
        }

        /// <summary>
        /// 包名下拉：行与置顶规则取自 <see cref="AssetPackagePopup"/>（带「留空」项，清单与 AssetServiceSettings 的
        /// YooAsset 包名选择同源）；清单未就绪时置灰占位。与 AssetReferenceDrawer 同款。
        /// </summary>
        private static void DrawPackagePopup(SerializedProperty packageProp)
        {
            if (packageProp == null)
            {
                return;
            }

            GUIContent[] displayed = AssetPackagePopup.Build(packageProp.stringValue, true, out int index, out bool manifestEmpty);

            using (new EditorGUI.DisabledScope(manifestEmpty))
            {
                GUILayout.Label(s_PackageContent, GUILayout.Width(56f));

                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(index, displayed, GUILayout.MaxWidth(180));
                if (EditorGUI.EndChangeCheck() && !manifestEmpty && selected >= 0 && selected < displayed.Length)
                {
                    packageProp.stringValue = AssetPackagePopup.Pick(displayed, true, selected);
                    WriteThrough(packageProp);
                }
            }
        }

        /// <summary>标签 tooltip 附资源解析路径；已有 tooltip 时保留（与 AssetReferenceDrawer 同款）。</summary>
        private static GUIContent BuildLabel(GUIContent label, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return label;
            }

            string tooltip = string.IsNullOrEmpty(label.tooltip) ? path : label.tooltip + "\n" + path;
            return new GUIContent(label.text, tooltip);
        }

        /// <summary>应用序列化改动并标脏目标资产（与 AssetReferenceDrawer 同口径）。</summary>
        private static void WriteThrough(SerializedProperty property)
        {
            property.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(property.serializedObject.targetObject);
        }

        /// <summary>
        /// 图集精灵数：精灵表（spriteInfoList）非空取其数，否则退回字形表（spriteGlyphTable）——
        /// 旧版 CurrencyPane 同款回退，本项目部分图集只填了字形表。
        /// </summary>
        private static int GetSpriteCount(TMP_SpriteAsset spriteAsset)
        {
            if (spriteAsset == null)
            {
                return 0;
            }

            if (spriteAsset.spriteInfoList != null && spriteAsset.spriteInfoList.Count > 0)
            {
                return spriteAsset.spriteInfoList.Count;
            }

            return spriteAsset.spriteGlyphTable?.Count ?? 0;
        }

        /// <summary>当前下标命中的精灵；越界或缺失时为 null。精灵表非空走精灵表，否则退回字形表。</summary>
        private static Sprite GetSpriteAt(TMP_SpriteAsset spriteAsset, int index)
        {
            if (spriteAsset == null || index < 0)
            {
                return null;
            }

            if (spriteAsset.spriteInfoList != null && spriteAsset.spriteInfoList.Count > 0)
            {
                return index < spriteAsset.spriteInfoList.Count ? spriteAsset.spriteInfoList[index]?.sprite : null;
            }

            return spriteAsset.spriteGlyphTable != null && index < spriteAsset.spriteGlyphTable.Count
                ? spriteAsset.spriteGlyphTable[index]?.sprite
                : null;
        }

        /// <summary>把精灵按纹理坐标等比缩放画进预览框（相对框中心对齐——框由调用方平移到字段块中心，斜放图形不因透明边距显得偏下）。</summary>
        private static void DrawSprite(Rect position, Sprite sprite)
        {
            Texture texture = sprite.texture;
            if (texture == null)
            {
                return;
            }

            Vector2 fullSize = new Vector2(texture.width, texture.height);
            Vector2 size = sprite.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            Rect coords = new Rect(sprite.rect.x / fullSize.x, sprite.rect.y / fullSize.y,
                sprite.rect.width / fullSize.x, sprite.rect.height / fullSize.y);
            Rect frame = position;
            float minRatio = Mathf.Min(frame.width / size.x, frame.height / size.y);
            position.width = size.x * minRatio;
            position.height = size.y * minRatio;
            position.center = frame.center;
            GUI.DrawTextureWithTexCoords(position, texture, coords);
        }
    }
}
