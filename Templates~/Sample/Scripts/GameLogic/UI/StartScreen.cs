using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.GameLogic.UI
{
	[Window(EUILayer.UI)]
	public partial class StartScreen : UGUIWindow<string>
	{
		protected override void OnRefresh()
		{
			_tmpInfo.text = Payload;
		}
	}
}