using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 二维向量 UI 动作接口。
    /// </summary>
    public interface IUIVector2Action : IUIAction
    { 
        /// <summary>
        /// 获取动作携带的二维向量值。
        /// </summary>
        Vector2 Vector2Value { get; }
    }
}