using System;
using Moirai.Atropos.Resource;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Localization
{
	public abstract class LocalizerBase : MonoBehaviour
	{
		protected ILocalizationInjector _injector;

		protected virtual void Awake()
		{
			LocalizationService.AddLocalizer(this);
			// 编辑态预览可能已补建过注入器；进 Play 走运行期路径，旧实例先释放再重建
			(_injector as IDisposable)?.Dispose();
			_injector = null;
			Prepare();
		}

		protected virtual void Start()
		{
			Localize();
		}

		protected virtual void OnDestroy()
		{
			LocalizationService.RemoveLocalizer(this);
			// 释放注入器持有的资源租约（如图片/音频注入器）
			(_injector as IDisposable)?.Dispose();
		}

		/// <summary>
		/// 准备对目标组件的引用。
		/// </summary>
		protected abstract void Prepare();

		/// <summary>
		/// 本地化目标组件。
		/// </summary>
		internal abstract void Localize();

		/// <summary>
		/// 本地化数据是否就绪。
		/// </summary>
		/// <remarks>未就绪时注入应静默推迟：首次加载成功触发的语言切换会重注入全部已注册本地化器。
		/// 「未就绪」与「词条真缺失」必须分开——前者不该按缺译刷错误日志。</remarks>
		protected static bool IsLocalizationDataReady => LocalizationService.IsDataLoaded;

#if UNITY_EDITOR
		/// <summary>
		/// 手动触发一次 <see cref="Prepare"/> 的接缝：EditMode 不跑 <c>Awake</c>，测试与编辑器面只能显式补这一步。
		/// </summary>
		internal void Internal_Prepare() => Prepare();

		/// <summary>
		/// Inspector 预览用的可读文本（当前 ID 解析出的译文 / 资源名）。
		/// </summary>
		/// <remarks>只在编辑器里跑，取不到数据时返回 <c>null</c> 由绘制侧退化为显示 ID。
		/// 刻意<strong>不</strong>把预览写回目标组件：那会把场景标脏并留下"忘了还原"的错文案。</remarks>
		internal virtual string GetPreviewDescriptor() => null;

		/// <summary>
		/// 编辑态为预览补一次 <see cref="Prepare"/>：只建注入器，不碰目标组件。
		/// </summary>
		/// <remarks>非播放态没有 <c>Awake</c>，注入器因此是空的；而预览要的「这个地址能不能用」
		/// 恰恰是注入器的判据，自己再写一份类型对照表迟早与注入器漂移。
		/// 找不到目标组件时不记成功，允许组件后补上之后重试（几次 <c>TryGetComponent</c>，远比类型表分叉便宜）。</remarks>
		protected void EnsurePreparedForPreview()
		{
			if (_injector != null) return;
			Prepare();
		}

		/// <summary>预览用的语言标签（未解析出语言时为空串）。</summary>
		protected static string LanguageTag(Language language) => language != null ? $"[{language.Code}] " : "";

		/// <summary>预览用的语言：播放态是服务当前语言，编辑态是 Inspector 选的编辑器语言。</summary>
		protected static Language PreviewLanguage() => Application.isPlaying
			? LocalizationService.CurrentLanguage
			: LocalizationService.EditorPreviewLanguage;

		/// <summary>预览用的语言列下标（两侧都不可用时为 -1）。</summary>
		protected static int PreviewLanguageIndex() => Application.isPlaying
			? LocalizationService.CurrentLanguageIndex
			: LocalizationService.EditorPreviewLanguageIndex;

		/// <summary>预览取不到译文时的说明（分得开「表内无此 ID」与「该语言留空」）。</summary>
		internal static string DescribeUnresolvedPreview(string id, EPreviewResolveStatus status) => status switch
		{
			EPreviewResolveStatus.BlankCell => $"<{id}> 该语言留空",
			EPreviewResolveStatus.Unavailable => $"<{id}> 预览数据未就绪",
			_ => $"<{id}> 表内无此 ID",
		};

		/// <summary>
		/// 资源 ID 注入型本地化器的预览：ID → 表内译文（即资源 location）→ 该 location 指向的资产。
		/// </summary>
		/// <remarks>
		/// 编辑态直读资产库是为了让「location 写错 / 资产没入库 / 类型不对」在 Inspector 里就看见——
		/// 这三条在运行期只表现为「图没出来」，得逐个点开日志才归因。
		/// 播放态不读资产库：那时注入器已按后端取过一份，再从库里另取一份等于替预览编一条运行期不走的路径。
		/// </remarks>
		/// <param name="id">词条 ID（其译文即资源定位地址 location）。</param>
		/// <param name="policy">注入器给出的类型判据；为 <c>null</c> 时不查资产，只报 location。</param>
		internal static string DescribeResourceIdPreview(string id, IInjectorAssetPreview policy = null)
		{
			if (string.IsNullOrEmpty(id)) return null;

			var status = LocalizationService.ResolvePreviewText(id, out var location, out var language);
			if (status != EPreviewResolveStatus.Resolved)
				return LanguageTag(language) + DescribeUnresolvedPreview(id, status);

			var tag = $"{LanguageTag(language)}{id} → {location}";
			if (policy == null) return tag + "（资源模式：注入器按该 location 取资源）";
			if (Application.isPlaying) return tag + "（运行期按租约加载）";

			var asset = ResourceService.LoadAssetForEditor(location);
			if (asset == null) return tag + " ✗ location 指向的资产取不到";

			var described = $"{tag} → {asset.GetType().Name} '{asset.name}'";
			if (policy.Accepts(asset)) return described;
			if (policy.Converts(asset)) return described + "（走自动转换路径，运行期会告警一次）";
			return described + $"（类型不符，注入器会拒绝：期望 {policy.ExpectedTypeName}）";
		}

		/// <summary>
		/// 资源模式预览的完整一趟：编辑态补建注入器，再按它给的类型判据把地址报到资产一层。
		/// </summary>
		/// <remarks>判据从注入器取而不是本地器自己判类型：<see cref="Prepare"/> 依据目标组件挑注入器，
		/// 同一份本地化器换组件就换类型，本地器侧写不出不漂移的第二份对照表。</remarks>
		protected string DescribeResourcePreview(string id)
		{
			EnsurePreparedForPreview();
			return DescribeResourceIdPreview(id, _injector as IInjectorAssetPreview);
		}

		/// <summary>按语言索引注入的预览行头：点名这一行用的是哪门语言、落到哪个下标。</summary>
		protected static string IndexedPreviewHeader(int index) => $"[{PreviewLanguage().Code}] 索引 {index} → ";

		/// <summary>按语言索引注入的数组在该下标上的元素概况——"新增语言后数组没补齐"这类错位只能在编辑器里先看见。</summary>
		internal static string DescribeIndexedElement<T>(T[] items, int index) where T : UObject
		{
			if (items == null || (uint)index >= (uint)items.Length) return "缺项";
			return items[index] == null ? "空引用" : items[index].name;
		}
#endif
	}
}
