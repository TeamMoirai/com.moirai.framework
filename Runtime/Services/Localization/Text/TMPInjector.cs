#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
using TMPro;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// TMP 文本本地化注入器，将本地化字符串写入 <see cref="TMP_Text"/> 组件。
	/// <para>仅在安装 TextMeshPro 或 uGUI 2 包（定义对应宏）后编译。</para>
	/// </summary>
	public class TMPInjector : IInjector
	{
		readonly TMP_Text tmp;

		/// <summary>
		/// 创建针对指定 <see cref="TMP_Text"/> 的本地化文本注入器。
		/// </summary>
		/// <param name="tmp">目标 <see cref="TMP_Text"/> 组件。</param>
		public TMPInjector(TMP_Text tmp)
		{
			this.tmp = tmp;
		}

		/// <summary>
		/// 将本地化数据转换为字符串并写入目标 <see cref="TMP_Text"/>；<paramref name="localizer"/> 参数未使用。
		/// </summary>
		/// <param name="localizedData">本地化数据，期望为字符串。</param>
		/// <param name="localizer">发起注入的本地化器，本实现未使用。</param>
		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			tmp.text = localizedData as string;
		}
	}
}
#endif
