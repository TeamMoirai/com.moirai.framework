using Moirai.Atropos.UI;
using UnityEngine;

namespace GameLogic.UI
{
	[Window(UILayer.UI)]
	public partial class StartScreen : UIWindow
	{
		protected override void OnRefresh()
		{
			_tmpInfo.text = (string)UserData;
		}
	}
}