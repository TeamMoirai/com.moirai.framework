namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 本地化数据注入器接口。
	/// </summary>
	public interface IInjector
	{
		/// <summary>
		/// 将本地化数据注入目标组件。
		/// </summary>
		/// <typeparam name="T1">本地化数据的类型。</typeparam>
		/// <typeparam name="T2">本地化器的类型。</typeparam>
		/// <param name="localizedData">待注入的本地化数据。</param>
		/// <param name="localizer">发起注入的本地化器。</param>
		void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase;
	}
}
