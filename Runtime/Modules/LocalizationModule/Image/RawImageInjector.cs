using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	public class RawImageInjector : IInjector
	{
		readonly string localizedTextID;
		readonly RawImage rawImage;
		readonly Texture[] textures;

		public RawImageInjector(RawImage rawImage, string localizedTextID, Texture[] textures)
		{
			this.localizedTextID = localizedTextID;
			this.rawImage = rawImage;
			this.textures = textures;
		}	
		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			if (localizedData is int index)
			{
				if (string.IsNullOrEmpty(localizedTextID))
				{
					rawImage.texture = textures[index];
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

			if (result is not Sprite or Texture)
			{
				Log.Error($"本地化图片类型错误，{textIDValue}");
			}
			
			if (result is Sprite sprite)
			{
				Texture texture = sprite.texture;
				rawImage.texture = texture;
				Log.Warning($"本地化图片类型错误，已自动转换：{result}");
				return;
			}
			rawImage.texture = result as Texture;
		}
	}
}
