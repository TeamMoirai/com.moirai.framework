using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 锁定inspector面板，再次按下相同的快捷方式即可解锁
    /// 快捷键 ctrl（或cmd）+ L
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

