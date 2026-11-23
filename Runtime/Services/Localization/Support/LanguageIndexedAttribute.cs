using System;
using System.Diagnostics;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 标记一个「下标 = 语言列下标」的数组字段，Inspector 里给每个数组元素行加语言码前缀。
    /// </summary>
    /// <remarks>
    /// 本地化模块专用标记：语义由本模块的「下标 = 语言列下标」约定承载（<see cref="LocalizationService.ExportedLanguages"/> 的读数来源）。 <br />
    /// 无多语言、清单比数组短时对应行保持无前缀，越界行不猜语言。 <br />
    /// 纯编辑器呈现标记，运行期不参与逻辑：下标语义仍由持有数组组件的注释与判据自述。
    /// </remarks>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LanguageIndexedAttribute : Attribute { }
}
