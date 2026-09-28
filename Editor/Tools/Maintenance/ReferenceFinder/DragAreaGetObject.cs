using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 编辑器窗口拖放区域的对象拖入处理工具。
    /// </summary>
    internal sealed class DragAreaGetObject
    {
        /// <summary>
        /// 处理当前拖放事件，在拖放执行（DragPerform）时返回拖入的对象数组。
        /// </summary>
        /// <param name="meg">可选消息参数，当前未使用。</param>
        /// <returns>拖入的对象数组；当前事件不是拖放完成时返回 <c>null</c>。</returns>
        public static Object[] GetObjects(string meg = null)
        {
            Event aEvent = Event.current;
            GUI.contentColor = Color.white;
            if (aEvent.type is EventType.DragUpdated or EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                bool needReturn = false;
                if (aEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    needReturn = true;
                }

                Event.current.Use();
                if (needReturn) return DragAndDrop.objectReferences;
            }

            return null;
        }
    }
}