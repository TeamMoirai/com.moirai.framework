using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入重绑定门面：按能力探测转发到当前后端，后端不支持重绑定时 fail-fast。
    /// </summary>
    /// <remarks>
    /// 能力面收窄在 <see cref="IInputRebindable"/>：旧 InputManager 与移动端 UI 后端不支持运行期改绑， <br />
    /// 访问即抛 <see cref="GameException"/>（带后端类型名），不做静默降级。
    /// </remarks>
    public partial class InputService
    {
        /// <summary>当前后端是否支持运行期重绑定（设置页据此显隐重绑入口）。</summary>
        public static bool SupportsRebinding => s_Handler is IInputRebindable;

        /// <summary>
        /// 获取当前后端的重绑定能力面。
        /// </summary>
        /// <exception cref="GameException">当前后端不支持运行期重绑定时抛出（含后端类型名）。</exception>
        public static IInputRebindable Rebinding
        {
            get
            {
                if (s_Handler is IInputRebindable rebindable) return rebindable;

                string backendName = s_Handler != null ? s_Handler.GetType().Name : "<未注册>";
                throw new GameException($"输入后端 {backendName} 不支持运行期重绑定（IInputRebindable）。");
            }
        }

        /// <summary>
        /// 交互式重绑的直通入口（等价 <c>Rebinding.RebindAsync</c>，后端不支持时抛出）。
        /// </summary>
        /// <param name="actionName">目标动作名（与查询 API 同寻址口径）。</param>
        /// <param name="actionGroup">目标动作分组（空串为全局）。</param>
        /// <param name="options">重绑选项（冲突策略/屏蔽鼠标触屏/复合部件定位）。</param>
        /// <param name="cancelToken">取消令牌：触发即结束监听并取消本次重绑。</param>
        /// <returns>终态快照（成功/取消/冲突/不可重绑）。</returns>
        /// <exception cref="GameException">当前后端不支持运行期重绑定时抛出。</exception>
        public static UniTask<InputRebindResult> RebindAsync(string actionName, string actionGroup = "",
            InputRebindOptions options = default, CancellationToken cancelToken = default) =>
            Rebinding.RebindAsync(actionName, actionGroup, options, cancelToken);
    }
}
