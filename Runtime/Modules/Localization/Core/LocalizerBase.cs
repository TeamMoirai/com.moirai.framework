using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public abstract class LocalizerBase : MonoBehaviour
	{
		protected IInjector _injector;

		protected virtual void Awake()
		{
			GameModule.Localization?.AddLocalizer(this);
			Prepare();
		}

		protected virtual void Start()
		{
			Localize();
		}

		protected virtual void OnDestroy()
		{
			GameModule.Localization?.RemoveLocalizer(this);
		}

		/// <summary>
		/// 准备对目标组件的引用。
		/// </summary>
		protected abstract void Prepare();

		/// <summary>
		/// 本地化目标组件。
		/// </summary>
		internal abstract void Localize();
	}
}
