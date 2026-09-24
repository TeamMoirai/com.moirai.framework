using System;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
	public abstract class LocalizerBase : MonoBehaviour
	{
		protected IInjector _injector;

		protected virtual void Awake()
		{
			LocalizationService.AddLocalizer(this);
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
		/// Inspector 预览用的可读文本（当前 ID 解析出的译文 / 资源名）。
		/// </summary>
		/// <remarks>只在编辑器里跑，取不到数据时返回 <c>null</c> 由绘制侧退化为显示 ID。
		/// 刻意<strong>不</strong>把预览写回目标组件：那会把场景标脏并留下"忘了还原"的错文案。</remarks>
		internal virtual string GetPreviewDescriptor() => null;

		/// <summary>资源 ID 注入型本地化器的预览：点明该 ID 在表内的文本（图/音按该地址异步加载）。</summary>
		internal static string DescribeResourceIdPreview(string id)
		{
			if (string.IsNullOrEmpty(id)) return null;

			return LocalizationService.EditorPreviewHasText(id)
				? $"{id} → {LocalizationService.ResolveForEditorPreview(id)}（资源模式：注入器按该 ID 异步加载）"
				: $"<{id}> 表内无此 ID";
		}

		/// <summary>按语言索引注入的数组在该下标上的元素概况——"新增语言后数组没补齐"这类错位只能在编辑器里先看见。</summary>
		internal static string DescribeIndexedElement<T>(T[] items, int index) where T : UnityEngine.Object
		{
			if (items == null || (uint)index >= (uint)items.Length) return "缺项";
			return items[index] == null ? "空引用" : items[index].name;
		}
#endif
	}
}
