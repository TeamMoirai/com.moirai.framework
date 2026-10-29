using System;
using UnityEngine;

namespace Moirai.Atropos.Bindings.Unity
{
    public sealed class SpriteBindingTarget : IBindingTarget
    {
        private readonly SpriteRenderer _renderer;

        public SpriteBindingTarget(SpriteRenderer renderer)
        {
            _renderer = renderer != null ? renderer : throw new ArgumentNullException(nameof(renderer));
        }

        public bool IsAlive => _renderer != null;

        public void Apply(object asset)
        {
            if (_renderer == null) throw new InvalidOperationException("Binding target is not alive.");
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (!(asset is Sprite sprite))
                throw new ArgumentException("Sprite binding target requires a Sprite.", nameof(asset));
            _renderer.sprite = sprite;
        }
    }
}
