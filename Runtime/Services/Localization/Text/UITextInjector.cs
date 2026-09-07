using UnityEngine.UI;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// uGUI 文本本地化注入器，将本地化字符串写入 <see cref="Text"/> 组件。
	/// </summary>
	public class UITextInjector : IInjector
	{
		readonly Text uiText;

		/// <summary>
		/// 创建针对指定 <see cref="Text"/> 的本地化文本注入器。
		/// </summary>
		/// <param name="uiText">目标 <see cref="Text"/> 组件。</param>
		public UITextInjector(Text uiText)
		{
			this.uiText = uiText;
		}

		/// <summary>
		/// 将本地化数据转换为字符串并写入目标 <see cref="Text"/>；<paramref name="localizer"/> 参数未使用。
		/// </summary>
		/// <param name="localizedData">本地化数据，期望为字符串。</param>
		/// <param name="localizer">发起注入的本地化器，本实现未使用。</param>
		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			uiText.text = localizedData as string;
		}
	}
}
