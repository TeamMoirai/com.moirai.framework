using UnityEngine;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// <see cref="TextMesh"/> 本地化注入器，将本地化字符串写入 3D 文本组件。
	/// </summary>
	public class TextMeshInjector : IInjector
	{
		readonly TextMesh textMesh;

		/// <summary>
		/// 创建针对指定 <see cref="TextMesh"/> 的本地化文本注入器。
		/// </summary>
		/// <param name="textMesh">目标 <see cref="TextMesh"/> 组件。</param>
		public TextMeshInjector(TextMesh textMesh)
		{
			this.textMesh = textMesh;
		}

		/// <summary>
		/// 将本地化数据转换为字符串并写入目标 <see cref="TextMesh"/>；<paramref name="localizer"/> 参数未使用。
		/// </summary>
		/// <param name="localizedData">本地化数据，期望为字符串。</param>
		/// <param name="localizer">发起注入的本地化器，本实现未使用。</param>
		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			textMesh.text = localizedData as string;
		}
	}
}
