using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 选择资源的位置
    /// </summary>
    [Flags]
    public enum EPick
    {
        Assets = 1 << 0,
        Scene = 1 << 1,
    }
    
    /// <summary>要填充选择资源的属性</summary>
    /// <list type="table">
    /// <item><term>Resource</term><description>选择资源的 Resource 路径</description></item>
    /// <item><term>AssetDatabase</term><description>选择资源的 AssetDatabase 路径</description></item>
    /// <item><term>Guid</term><description>初选择资源的 GUID</description></item>
    /// </list>
    public enum EStr
    {
        Resource,
        AssetDatabase,
        Guid,
    }
    
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class ResourcePathAttribute : PropertyAttribute
    {
        public readonly Type RequiredType;
        public readonly EPick EditorPick;
        public readonly EStr EStr;

        public ResourcePathAttribute(EStr eStr, Type requiredType, EPick editorPick = EPick.Assets)
        {
            EStr = eStr;
            RequiredType = requiredType;
            EditorPick = editorPick;
        }
    }
}