#if TIMELINE_INSTALLED
using UnityEngine;
using UnityEngine.Playables;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 文本本地化时间线可播放资源（PlayableAsset），用于在 Timeline 轨道中携带本地化文本 ID。
	/// </summary>
	[System.Serializable]
	public class TextLocalizerPlayableAsset : PlayableAsset
	{
		/// <summary>
		/// 本地化文本 ID，创建可播放实例时会被传递给 <see cref="TextLocalizerPlayableBehaviour"/>。
		/// </summary>
		public string textId;

		/// <summary>
		/// 创建承载 <see cref="TextLocalizerPlayableBehaviour"/> 的可播放实例，并将 <see cref="textId"/> 赋给该行为。
		/// </summary>
		/// <param name="graph">承载该可播放实例的 <see cref="PlayableGraph"/>。</param>
		/// <param name="go">拥有该轨道播放器的 GameObject。</param>
		/// <returns>新创建的可播放实例，其行为为 <see cref="TextLocalizerPlayableBehaviour"/>。</returns>
		public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
		{
			var playable = ScriptPlayable<TextLocalizerPlayableBehaviour>.Create(graph);

			var behaviour = playable.GetBehaviour();
			behaviour.textId = textId;

			return playable;
		}
	}
}
#endif