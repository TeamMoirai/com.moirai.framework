using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    public class SpriteRendererInjector : IInjector
    {
        readonly string localizedTextID;
        readonly SpriteRenderer spriteRenderer;
        readonly Sprite[] sprites;

        public SpriteRendererInjector(SpriteRenderer spriteRenderer, string localizedTextID, Sprite[] sprites)
        {
            this.localizedTextID = localizedTextID;
            this.spriteRenderer = spriteRenderer;
            this.sprites = sprites;
        }

        public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
        {
            if (localizedData is int index)
            {
                if (string.IsNullOrEmpty(localizedTextID))
                {
                    spriteRenderer.sprite = sprites[index];
                }
                else
                {
                    ApplyFromResource().Forget();
                }
            }
        }

        private async UniTaskVoid ApplyFromResource()
        {
            string textIDValue = GameModule.Localization.GetTextFromId(localizedTextID);
            var result = await GameModule.Resource.LoadAssetAsync<UnityEngine.Object>(textIDValue);

            if (result is not Sprite or Texture2D)
            {
                Log.Error($"本地化图片类型错误，{textIDValue}");
            }
            
            if (result is Texture2D texture)
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                spriteRenderer.sprite = sprite;
                Log.Warning($"本地化图片类型错误，已自动转换：{result}");
                return;
            }
            spriteRenderer.sprite = result as Sprite;
        }
    }
}

