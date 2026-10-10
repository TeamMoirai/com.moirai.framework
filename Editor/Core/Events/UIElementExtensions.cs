using System;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 表示未找到指定名称的 VisualElement 时抛出的异常。
    /// </summary>
    internal class MissingVisualElementException : Exception
    {
        /// <summary>
        /// 创建默认的异常实例。
        /// </summary>
        public MissingVisualElementException() { }

        /// <summary>
        /// 使用指定消息创建异常实例。
        /// </summary>
        /// <param name="message">异常消息。</param>
        public MissingVisualElementException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// <see cref="VisualElement"/> 与 <see cref="IPanel"/> 的查询扩展方法集。
    /// </summary>
    public static class UIElementExtensions
    {
        /// <summary>
        /// 获取面板视觉树中索引 1 处的子元素（约定为内容根）。
        /// </summary>
        /// <param name="panel">目标面板。</param>
        /// <returns>内容根元素；面板为 <c>null</c> 或视觉树仅有单个子元素时返回 <c>null</c>。</returns>
        public static VisualElement GetRootVisualElement(this IPanel panel)
        {
            if (panel == null)
            {
                return null;
            }

            VisualElement visualTree = panel.visualTree;
            if (visualTree.childCount == 1)
            {
                return null;
            }

            return visualTree[1];
        }

        /// <summary>
        /// 按名称与可选类名查询子元素，找不到时抛出 <see cref="MissingVisualElementException"/>。
        /// </summary>
        /// <param name="e">查询的根元素。</param>
        /// <param name="name">元素名称。</param>
        /// <param name="className">可选的类名过滤。</param>
        /// <typeparam name="T">期望的元素类型。</typeparam>
        /// <returns>查询到的元素。</returns>
        public static T MandatoryQ<T>(this VisualElement e, string name, string className = null) where T : VisualElement
        {
            var element = e.Q<T>(name, className) ?? throw new MissingVisualElementException("Element not found: " + name);
            return element;
        }

        /// <summary>
        /// 按名称与可选类名查询子元素，找不到时抛出 <see cref="MissingVisualElementException"/>。
        /// </summary>
        /// <param name="e">查询的根元素。</param>
        /// <param name="name">元素名称。</param>
        /// <param name="className">可选的类名过滤。</param>
        /// <returns>查询到的元素。</returns>
        public static VisualElement MandatoryQ(this VisualElement e, string name, string className = null)
        {
            var element = e.Q<VisualElement>(name, className) ?? throw new MissingVisualElementException("Element not found: " + name);
            return element;
        }
    }
}