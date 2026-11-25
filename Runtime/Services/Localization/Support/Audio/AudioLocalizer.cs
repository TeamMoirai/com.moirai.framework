using Cysharp.Threading.Tasks;
using Moirai.Atropos.Asset;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public class AudioLocalizer : LocalizerBase
	{
		[SerializeField] private string m_LocalizedTextID = "";

		[Tooltip("按语言索引的音频资源软引用：下标 = 当前语言列下标（下标越界报错；空元素清空音源）")]
		[SerializeField, LanguageIndexed] internal AssetReference<AudioClip>[] m_Clips;

		[SerializeField] private bool m_PlayFromSamePositionWhenInject;
		public bool PlayFromSamePositionWhenInject => m_PlayFromSamePositionWhenInject;

		protected override void Prepare()
		{
			var component = ComponentFinder.Find<AudioSource>(this);
			if (component == null) return;

			if (component is AudioSource audio)
			{
				_injector = new AudioSourceInjector(audio);
			}
		}

#if UNITY_EDITOR
		internal override string GetPreviewDescriptor()
		{
			if (!string.IsNullOrEmpty(m_LocalizedTextID)) return DescribeAssetPreview(m_LocalizedTextID);

			var index = PreviewLanguageIndex();
			if (index < 0) return null;

			return IndexedPreviewHeader(index) + $"clips: {DescribeIndexedElement(m_Clips, index)}";
		}
#endif

		internal override void Localize()
		{
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error("AudioLocalizer {0}: no AudioSource found.", name);
				return;
			}

			// 数据未就绪（表未加载完）：静默推迟，首载成功的语言切换会重注入——「未就绪」不按缺译报错
			if (!IsLocalizationDataReady) return;

			// 资源模式（配置了文本 ID）下 clips 数组可空：单趟解析出 location 作为载荷交注入器异步加载
			if (!string.IsNullOrEmpty(m_LocalizedTextID))
			{
				if (!LocalizationService.TryGetTextFromId(m_LocalizedTextID, out var location))
				{
					if (Application.isPlaying) LogUtility.Error("Text ID: {0} is incorrect.", m_LocalizedTextID);
					return;
				}

				_injector.Inject(location, this);
				return;
			}

			var index = LocalizationService.CurrentLanguageIndex;
			if (index < 0) return;

			if (m_Clips == null || index >= m_Clips.Length)
			{
				LogUtility.Error("AudioLocalizer {0}: clips has no entry for language index {1}.", name, index);
				return;
			}

			// 单趟取片段直注：不走注入器 string-location 路径（裸 LoadLeaseAsync 未就绪会 fail-fast）
			var reference = m_Clips[index];
			if (reference == null || string.IsNullOrEmpty(reference.GUID))
			{
				// 该语言没配语音是正常态：清空音源；null 载荷需显式类型实参
				_injector.Inject<AudioClip, AudioLocalizer>(null, this);
				return;
			}

			ApplyIndexedClipAsync(reference).Forget();
		}

		/// <summary>异步取语言索引的音频片段并直注播放。</summary>
		private async UniTaskVoid ApplyIndexedClipAsync(AssetReference<AudioClip> reference)
		{
			var clip = await LoadIndexedAsync(reference, this.GetCancellationTokenOnDestroy());
			if (_injector == null) return; // 销毁后在途结果丢弃

			_injector.Inject(clip, this);
		}

		public bool ChangeID(string textId)
		{
			if (string.IsNullOrEmpty(textId)) return false;
			// 同 ID 早退：避免重复异步加载；语言切换走 Localize() 仍会重刷
			if (textId == m_LocalizedTextID) return true;
#if UNITY_EDITOR
			// 非播放态不注入：编辑态没有后端可取资产，写进组件还会把场景标脏
			if (!Application.isPlaying) return false;
#endif
			if (!LocalizationService.TryGetTextFromId(textId, out var location))
			{
				if (Application.isPlaying) LogUtility.Error("Text ID: {0} is incorrect.", textId);
				return false;
			}

			m_LocalizedTextID = textId;

			// 与 TextLocalizer.ChangeID 语义一致：记录后立即应用（location 已单趟解析，直接注入）
			if (_injector == null)
			{
				if (Application.isPlaying) LogUtility.Error("AudioLocalizer {0}: no AudioSource found.", name);
				return true;
			}

			_injector.Inject(location, this);
			return true;
		}

		public void Clear()
		{
			m_LocalizedTextID = null;
			_injector?.Clear();
		}
	}
}
