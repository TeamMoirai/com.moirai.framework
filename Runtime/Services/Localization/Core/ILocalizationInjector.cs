namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 本地化数据注入器接口。
	/// </summary>
	public interface ILocalizationInjector
	{
		/// <summary>
		/// 将本地化数据注入目标组件。载荷类型的语义由具体注入器自定：文本注入器把 <c>string</c> 当译文；
		/// 资源类注入器（图片 / 音频）按 <c>int</c> 语言下标、<c>string</c> 资源 location、资产直注派发。
		/// </summary>
		/// <typeparam name="T1">本地化数据的类型。</typeparam>
		/// <typeparam name="T2">本地化器的类型。</typeparam>
		/// <param name="localizedData">待注入的本地化数据。</param>
		/// <param name="localizer">发起注入的本地化器。</param>
		void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase;

		/// <summary>
		/// 清除已注入内容，并释放注入器持有的资源租约（若有）。
		/// </summary>
		void Clear();
	}
}
