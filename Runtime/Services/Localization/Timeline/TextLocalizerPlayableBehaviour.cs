#if TIMELINE_INSTALLED
using UnityEngine.Playables;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 文本本地化时间线可播放行为，在片段播放期间将 <see cref="textId"/> 应用到绑定的 <see cref="TextLocalizer"/>，暂停时清除已应用内容。
	/// </summary>
	public class TextLocalizerPlayableBehaviour : PlayableBehaviour
	{
		/// <summary>
		/// 本地化文本 ID，由 <see cref="TextLocalizerPlayableAsset"/> 传入。
		/// </summary>
		public string textId;
		/// <summary>
		/// 当前绑定的 <see cref="TextLocalizer"/> 组件，来自轨道的 playerData。
		/// </summary>
		TextLocalizer textLocalizer;

		/// <summary>
		/// 行为进入播放时的回调，本实现为空操作。
		/// </summary>
		/// <param name="playable">该行为的可播放对象。</param>
		/// <param name="info">当前帧的帧数据。</param>
		public override void OnBehaviourPlay(Playable playable, FrameData info) { }

		/// <summary>
		/// 行为暂停时的回调，若已绑定 <see cref="TextLocalizer"/> 则调用其 <c>Clear()</c> 清除本地化文本。
		/// </summary>
		/// <param name="playable">该行为的可播放对象。</param>
		/// <param name="info">当前帧的帧数据。</param>
		public override void OnBehaviourPause(Playable playable, FrameData info)
		{
			if (textLocalizer != null) textLocalizer.Clear();
		}

		/// <summary>
		/// 每帧处理：从 <paramref name="playerData"/> 获取 <see cref="TextLocalizer"/>，并调用其 <c>ChangeID</c> 应用 <see cref="textId"/>。
		/// </summary>
		/// <param name="playable">该行为的可播放对象。</param>
		/// <param name="info">当前帧的帧数据。</param>
		/// <param name="playerData">轨道绑定的数据对象，期望为 <see cref="TextLocalizer"/>。</param>
		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			textLocalizer = playerData as TextLocalizer;
			if (textLocalizer != null) textLocalizer.ChangeID(textId);
		}
	}
}
#endif
