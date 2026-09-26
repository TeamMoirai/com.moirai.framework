#if UNITY_EDITOR
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 注入器供 Inspector 预览使用的类型判据接缝。
    /// <para>「这个地址指向的资产能不能用」只有注入器说得准：它加载期收哪一种、哪一种走自动转换，
    /// 预览就得按同一条回答。组件侧另写一份类型对照表，迟早与注入器分叉成「预览说没问题、运行期报类型错」。</para>
    /// <para>刻意不做成公开 API，也不做成注入器的运行期成员：预览是编辑器面的诊断，
    /// 运行期取资产仍只有一条租约路径。</para>
    /// </summary>
    internal interface IInjectorAssetPreview
    {
        /// <summary>注入器直接接受的那种类型的名字，用于预览点名期望；实现一律给 <c>nameof(类型)</c>，不写字面量。</summary>
        string ExpectedTypeName { get; }

        /// <summary>这份资产能否原样注入。</summary>
        bool Accepts(UObject asset);

        /// <summary>这份资产是否要走自动转换路径；没有转换这回事的注入器返回 <c>false</c>。</summary>
        bool Converts(UObject asset);
    }
}
#endif
