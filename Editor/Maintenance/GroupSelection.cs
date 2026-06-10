using UnityEngine;
using UnityEditor;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 在父游戏对象下添加菜单项和将对象分组在一起的类
    /// 快捷键 ctrl（或cmd）+ G
    /// </summary>
    public static class GroupSelection 
    {
        /// <summary>
        /// 创建一个父对象并将所选 Gameobjects 置于其下
        /// </summary>
        [MenuItem("Tools/Group Selection %g")]
        public static void Process()
        {
            if (!Selection.activeTransform)
            {
                return;
            }

            GameObject groupObject = new GameObject();
            groupObject.name = "Group";

            Undo.RegisterCreatedObjectUndo(groupObject, "Group Selection");

            groupObject.transform.SetParent(Selection.activeTransform.parent, false);

            foreach (Transform selectedTransform in Selection.transforms)
            {
                Undo.SetTransformParent(selectedTransform, groupObject.transform, "Group Selection");
            }
            Selection.activeGameObject = groupObject;
        }
    }
}
