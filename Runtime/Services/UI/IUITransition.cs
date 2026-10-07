using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 窗口开/关过渡：与窗口解耦的播放契约，瞬时档由 <see cref="UIWindow.Transition"/> 缺位表达。
    /// </summary>
    /// <remarks>
    /// <see cref="Play"/> 只在过渡期间被等待：过渡中窗口锁交互（模态窗还占全局压制位）， <br />
    /// 被（重开/销毁/新一轮过渡）接管时按取消令牌掐断，代次守卫由窗口那一侧接办，实现不必自理作废。 <br />
    /// <see cref="Snap"/> 供跳过等待的路径把面板当场拨到终态，不再逐帧补播。线程契约：仅主线程。
    /// </remarks>
    public interface IUITransition
    {
        /// <summary>
        /// 播放一段开/关过渡并等它走完。
        /// </summary>
        /// <param name="open">开窗方向为真，关窗方向为假。</param>
        /// <param name="cancellationToken">窗口代次取消令牌：接管或销毁时掐断在播过渡。</param>
        /// <returns>过渡走完。</returns>
        UniTask Play(bool open, CancellationToken cancellationToken);

        /// <summary>
        /// 把面板当场拨到开/关终态：跳过等待的路径用它。
        /// </summary>
        /// <param name="open">开窗终态为真，关窗终态为假。</param>
        void Snap(bool open);
    }
}
