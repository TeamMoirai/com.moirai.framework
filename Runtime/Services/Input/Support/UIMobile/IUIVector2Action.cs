using UnityEngine;

namespace Moirai.Atropos.Input
{
    public interface IUIVector2Action : IUIAction
    { 
        Vector2 Vector2Value { get; }
    }
}