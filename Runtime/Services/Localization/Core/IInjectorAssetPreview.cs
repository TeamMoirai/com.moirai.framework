#if UNITY_EDITOR
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 注入器供 Inspector 预览使用的类型判据接缝：以注入器的口径回答资产能否注入、是否走自动转换。
    /// </summary>
    /// <remarks>
    /// 实现须与注入器加载期的接受/转换判据一致，否则预览与运行期会分叉成「预览通过、运行期报类型错」。 <br />
    /// 仅编辑器面诊断使用，不是公开 API；运行期取资产仍只有租约路径。
    /// </remarks>
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
