using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public class TextureInjector : IInjector
	{
		readonly string localizedTextID;
		readonly Renderer renderer;
		readonly string propertyName;
		readonly Texture2D[] texture2Ds;

		public TextureInjector(Renderer renderer, string localizedTextID, string propertyName, Texture2D[] texture2Ds)
		{
			this.localizedTextID = localizedTextID;
			this.renderer = renderer;
			this.propertyName = propertyName;
			this.texture2Ds = texture2Ds;
		}

		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			if (localizedData is int index)
			{
				if (string.IsNullOrEmpty(localizedTextID))
				{
					renderer.material.SetTexture(propertyName, texture2Ds[index]);
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
			
			if (result is Sprite sprite)
			{
				Texture2D texture = sprite.texture;
				renderer.material.SetTexture(propertyName, texture);
				Log.Warning($"本地化图片类型错误，已自动转换：{result}");
				return;
			}

			renderer.material.SetTexture(propertyName, result as Texture2D);
		}
	}
}
