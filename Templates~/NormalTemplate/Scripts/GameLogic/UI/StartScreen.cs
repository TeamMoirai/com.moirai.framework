using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.GameLogic.UI
{
	[Window(UILayer.UI)]
	public partial class StartScreen : UGUIWindow
	{
		protected override void OnRefresh()
		{
			_tmpInfo.text = (string)UserData;
		}
	}
}