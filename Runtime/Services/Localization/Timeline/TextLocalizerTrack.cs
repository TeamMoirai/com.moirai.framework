#if TIMELINE_INSTALLED
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Moirai.Atropos.Localization
{
	/// <summary>
	/// 文本本地化时间线轨道，绑定 <see cref="TextLocalizer"/> 组件，片段使用 <see cref="TextLocalizerPlayableAsset"/>。
	/// </summary>
	[TrackClipType(typeof(TextLocalizerPlayableAsset))]
	[TrackBindingType(typeof(TextLocalizer))]
	public class TextLocalizerTrack : TrackAsset
	{
		/// <summary>
		/// 创建轨道混合器前，遍历轨道上的所有片段，将片段显示名设置为资源中的本地化文本 ID，便于在 Timeline 窗口中识别。
		/// <para>注意：若片段资源无法转换为 <see cref="TextLocalizerPlayableAsset"/>，直接访问 <c>textId</c> 会引发空引用异常。</para>
		/// </summary>
		/// <param name="graph">承载该轨道的 <see cref="PlayableGraph"/>。</param>
		/// <param name="go">拥有该轨道播放器的 GameObject。</param>
		/// <param name="inputCount">该轨道的输入数量。</param>
		/// <returns>默认的轨道混合器可播放对象。</returns>
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			var clips = GetClips();
			foreach (var clip in clips)
			{
				var asset = clip.asset as TextLocalizerPlayableAsset;
				clip.displayName = asset.textId;
			}

			return base.CreateTrackMixer(graph, go, inputCount);
		}
	}
}
#endif
