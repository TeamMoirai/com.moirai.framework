#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
using TMPro;

namespace Moirai.Atropos.Localization
{
	public class TMPInjector : IInjector
	{
		readonly TMP_Text tmp;

		public TMPInjector(TMP_Text tmp)
		{
			this.tmp = tmp;
		}

		public void Inject<T1, T2>(T1 localizedData, T2 localizer) where T2 : LocalizerBase
		{
			tmp.text = localizedData as string;
		}
	}
}
#endif
