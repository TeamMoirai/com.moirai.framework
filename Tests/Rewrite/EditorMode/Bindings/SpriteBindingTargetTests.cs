using System;
using Moirai.Atropos.Bindings.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Bindings
{
    public sealed class SpriteBindingTargetTests
    {
        private GameObject _object;
        private SpriteRenderer _renderer;
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("SpriteBindingTargetTests");
            _renderer = _object.AddComponent<SpriteRenderer>();
            Texture2D texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sprite != null)
            {
                Texture2D texture = _sprite.texture;
                UnityEngine.Object.DestroyImmediate(_sprite);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
            if (_object != null) UnityEngine.Object.DestroyImmediate(_object);
        }

        [Test]
        public void Apply_AssignsSprite()
        {
            SpriteBindingTarget target = new SpriteBindingTarget(_renderer);

            target.Apply(_sprite);

            Assert.That(target.IsAlive, Is.True);
            Assert.That(_renderer.sprite, Is.SameAs(_sprite));
        }

        [Test]
        public void Apply_WithNonSprite_ThrowsAndKeepsRendererUnchanged()
        {
            SpriteBindingTarget target = new SpriteBindingTarget(_renderer);

            Assert.Throws<ArgumentException>(() => target.Apply("not a sprite"));
            Assert.That(_renderer.sprite, Is.Null);
        }

        [Test]
        public void DestroyedRenderer_ReportsNotAlive_AndApplyThrows()
        {
            SpriteBindingTarget target = new SpriteBindingTarget(_renderer);

            UnityEngine.Object.DestroyImmediate(_object);

            Assert.That(target.IsAlive, Is.False);
            Assert.Throws<InvalidOperationException>(() => target.Apply(_sprite));
        }
    }
}
