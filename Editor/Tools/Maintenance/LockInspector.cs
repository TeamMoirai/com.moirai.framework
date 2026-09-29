using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 锁定 Inspector 面板，再次按下相同快捷键即解锁（快捷键 <c>Ctrl</c> / <c>Cmd</c> + <c>L</c>）。
    /// </summary>
    public static class LockInspector
    {
        [MenuItem("Tools/Lock Inspector %l")]
        public static void Process()
        {
            Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            EditorWindow inspectorWindow = EditorWindow.GetWindow(inspectorType);

            PropertyInfo isLockedPropertyInfo = inspectorType.GetProperty("isLocked", BindingFlags.Public | BindingFlags.Instance);
            bool state = (bool)isLockedPropertyInfo.GetGetMethod().Invoke(inspectorWindow, new object[] { });

            isLockedPropertyInfo.GetSetMethod().Invoke(inspectorWindow, new object[] { !state });
        }
    }
}

