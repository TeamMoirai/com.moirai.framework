using System.Linq;
using Moirai.Atropos.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Audio.Editor.Drawer
{
    [CustomPropertyDrawer(typeof(AudioClipInfo), true)]
    public class AudioClipInfoDrawer : PropertyDrawer
    {
        private const string PATH_PROPERTY = "m_Path";
        private const string GUID_PROPERTY = "m_Guid";

        #region 引擎方法 [UNITY METHODS]

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => GetHeight(property);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (fieldInfo.GetCustomAttributes(typeof(TooltipAttribute), true).FirstOrDefault() is TooltipAttribute tt)
                label.tooltip = tt.tooltip;

            DrawInfo(position, property);
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        public static float GetHeight(SerializedProperty property) =>
            AssetInfoHelper.GetAssetInfoHeight<AudioClip>(
                property.FindPropertyRelative(GUID_PROPERTY).stringValue,
                property.FindPropertyRelative(PATH_PROPERTY).stringValue,
                1); // 显示默认1行
        
        public static void DrawInfo(Rect position, SerializedProperty property) =>
            AssetInfoHelper.DrawBaseAssetInfo<AudioClip>(ref position, property, GUID_PROPERTY, PATH_PROPERTY);
        
        #endregion

    }
}