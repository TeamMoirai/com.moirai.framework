using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Audio.Editor
{
    [CustomPropertyDrawer(typeof(AudioGroupConfig))]
    public class AudioGroupConfigDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 5 + 10f;
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
        
            // 隐藏默认标签
            position = EditorGUI.PrefixLabel(position, GUIContent.none);
        
            // 获取子属性
            var trackProp = property.FindPropertyRelative("m_AudioTrack");
            var mixerProp = property.FindPropertyRelative("m_AudioMixerGroup");

            var volumeProp = property.FindPropertyRelative("m_DefaultVolume");
            var mixerMultiProp = property.FindPropertyRelative("m_MixerValuesMultiplier");
            var maxChannelProp = property.FindPropertyRelative("m_MaxChannel");
            var canExpandProp = property.FindPropertyRelative("m_CanExpand");
            
            // 计算布局
            position.y += 10f;
            float width = position.width;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 2f;
            
            Rect trackRect = new Rect(position.x, position.y, width * 0.35f, 18);
            Rect mixerRect = new Rect(position.x + width * 0.37f, position.y, width * 0.63f, 18);
           
            Rect volumeRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight);
            Rect mixerMultiRect = new Rect(position.x, position.y + (lineHeight + spacing)*2, position.width, lineHeight);
            Rect channelRect = new Rect(position.x, position.y + (lineHeight + spacing)*3, position.width/2, lineHeight);
            Rect expandRect = new Rect(position.x + position.width/2 + spacing, position.y + (lineHeight + spacing)*3, position.width/2 - spacing, lineHeight);
            
            // 绘制字段
            EditorGUI.PropertyField(trackRect, trackProp, GUIContent.none);
            EditorGUI.PropertyField(mixerRect, mixerProp, GUIContent.none);
            
            // 绘制音量字段（带独立标签）
            EditorGUI.LabelField(volumeRect, "默认音量");
            volumeRect.x += width*0.37f ; // 标签宽度
            volumeRect.width -= width*0.37f;
            EditorGUI.Slider(volumeRect, volumeProp, 0f, AudioGroupConfig.MAXIMAL_VOLUME, GUIContent.none);

            // 绘制其他字段
            EditorGUI.PropertyField(mixerMultiRect, mixerMultiProp);
            EditorGUI.PropertyField(channelRect, maxChannelProp);
            EditorGUI.PropertyField(expandRect, canExpandProp);
            
            EditorGUI.EndProperty();
        }
    }
}