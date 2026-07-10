using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    internal sealed class HelperInfo<T> where T : MonoBehaviour
    {
        private const string CUSTOM_OPTION_NAME = "<Custom>";

        private readonly string m_Name;

        private SerializedProperty m_HelperTypeName;
        private SerializedProperty m_CustomHelper;
        private string[] m_HelperTypeNames;
        private int m_HelperTypeNameIndex;
        private string[] m_HelperTypeDisplayNames;

        public HelperInfo(string name)
        {
            m_Name = name;

            m_HelperTypeName = null;
            m_CustomHelper = null;
            m_HelperTypeNames = null;
            m_HelperTypeNameIndex = 0;
            m_HelperTypeDisplayNames = null;
        }

        public void Init(SerializedObject serializedObject)
        {
            m_HelperTypeName = serializedObject.FindProperty(StringUtility.Format("m_{0}HelperTypeName", m_Name));
            m_CustomHelper = serializedObject.FindProperty(StringUtility.Format("m_Custom{0}Helper", m_Name));
        }

        public void Draw()
        {
            string displayName = FieldNameForDisplay(m_Name);
            int selectedIndex = EditorGUILayout.Popup(StringUtility.Format("{0} Helper", displayName), m_HelperTypeNameIndex, m_HelperTypeDisplayNames);
            if (selectedIndex != m_HelperTypeNameIndex)
            {
                m_HelperTypeNameIndex = selectedIndex;
                m_HelperTypeName.stringValue = selectedIndex <= 0 ? null : m_HelperTypeNames[selectedIndex];
            }

            if (m_HelperTypeNameIndex <= 0)
            {
                EditorGUILayout.PropertyField(m_CustomHelper);
                if (m_CustomHelper.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(StringUtility.Format("You must set Custom {0} Helper.", displayName), MessageType.Error);
                }
            }
        }

        public void Refresh()
        {
            List<string> helperTypeNameList = new List<string>
            {
                CUSTOM_OPTION_NAME
            };

            helperTypeNameList.AddRange(TypeUtility.GetRuntimeTypeNames(typeof(T)));
            m_HelperTypeNames = helperTypeNameList.ToArray();
            m_HelperTypeDisplayNames = ExtractClassName(m_HelperTypeNames);

            m_HelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(m_HelperTypeName.stringValue))
            {
                m_HelperTypeNameIndex = helperTypeNameList.IndexOf(m_HelperTypeName.stringValue);
                if (m_HelperTypeNameIndex <= 0)
                {
                    m_HelperTypeNameIndex = 0;
                    m_HelperTypeName.stringValue = null;
                }
            }
        }

        private string FieldNameForDisplay(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                return string.Empty;
            }
            
            string str = Regex.Replace(fieldName, @"^m_", string.Empty);
            str = Regex.Replace(str, @"((?<=[a-z])[A-Z]|[A-Z](?=[a-z]))", @" $1").TrimStart();
            return str;
        }
        
        /// <summary>
        /// 从全名中提取类名
        /// </summary>
        /// <param name="fullNames"></param>
        /// <returns></returns>
        private static string[] ExtractClassName(string[] fullNames)
        {
            string[] result = new string[fullNames.Length];

            for (int i = 0; i < fullNames.Length; i++)
            {
                // 找到最后一个 . 的位置
                int lastDotIndex = fullNames[i].LastIndexOf('.');
        
                // 如果没有找到 .，则返回整个字符串
                if (lastDotIndex == -1)
                {
                    result[i] = fullNames[i];
                }
                
                // 提取最后一个 . 后的子字符串
                result[i] = fullNames[i].Substring(lastDotIndex + 1);
            }
            
            return result;
        }
    }
}
