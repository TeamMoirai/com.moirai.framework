using System;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 重绑定冲突的处置策略。
    /// </summary>
    public enum ERebindConflictPolicy : byte
    {
        /// <summary>检测到冲突即失败返回（缺省，最安全）。</summary>
        Fail = 0,

        /// <summary>自动移除其他动作上与新绑定的冲突项后再落绑。</summary>
        Override,

        /// <summary>检测到冲突时取消本次重绑，保留原绑定。</summary>
        Cancel,
    }

    /// <summary>
    /// 重绑定结果的终态类别。
    /// </summary>
    public enum ERebindOutcome : byte
    {
        /// <summary>重绑成功落地。</summary>
        Applied = 0,

        /// <summary>监听期被取消（用户按了取消控件或取消令牌触发）。</summary>
        Cancelled,

        /// <summary>检测到绑定冲突且策略不允许覆盖。</summary>
        Conflict,

        /// <summary>目标动作不可重绑（不存在、值为只读、绑定集合为空等）。</summary>
        NotRebindable,
    }

    /// <summary>
    /// 一次交互式重绑的终态快照。
    /// </summary>
    public readonly struct InputRebindResult
    {
        /// <summary>终态类别。</summary>
        public readonly ERebindOutcome Outcome;

        /// <summary>重绑目标动作分组（空串为全局）。</summary>
        public readonly string ActionGroup;

        /// <summary>重绑目标动作名。</summary>
        public readonly string ActionName;

        /// <summary>成功时为实际落绑的控件路径；非 <see cref="ERebindOutcome.Applied"/> 为 null。</summary>
        public readonly string BindingPath;

        /// <summary>与本次新绑定冲突的动作全限定名（Group/Name）；无冲突为 null。</summary>
        public readonly string ConflictedAction;

        /// <summary>
        /// 创建重绑结果。
        /// </summary>
        public InputRebindResult(ERebindOutcome outcome, string actionGroup, string actionName,
            string bindingPath = null, string conflictedAction = null)
        {
            Outcome = outcome;
            ActionGroup = actionGroup ?? string.Empty;
            ActionName = actionName ?? string.Empty;
            BindingPath = bindingPath;
            ConflictedAction = conflictedAction;
        }

        /// <summary>
        /// 构造终态结果（无绑定信息）。
        /// </summary>
        public static InputRebindResult Failed(ERebindOutcome outcome, string actionGroup, string actionName) =>
            new InputRebindResult(outcome, actionGroup, actionName);

        /// <summary>
        /// 构造冲突终态结果。
        /// </summary>
        public static InputRebindResult Conflict(string actionGroup, string actionName, string conflictedAction) =>
            new InputRebindResult(ERebindOutcome.Conflict, actionGroup, actionName, null, conflictedAction);
    }

    /// <summary>
    /// 交互式重绑的可选项。
    /// </summary>
    public struct InputRebindOptions
    {
        /// <summary>冲突处置策略（缺省 <see cref="ERebindConflictPolicy.Fail"/>）。</summary>
        public ERebindConflictPolicy ConflictPolicy;

        /// <summary>监听期是否屏蔽鼠标与触屏（设置页由指针操作，缺省屏蔽避免误落绑）。</summary>
        public bool ExcludeMouse;

        /// <summary>复合绑定的部件名（如 WASD 的 up/down/left/right；空 = 单控绑定整体）。</summary>
        public string PartName;

        /// <summary>缺省选项：屏蔽鼠标触屏、冲突即失败。</summary>
        public static InputRebindOptions Default => new InputRebindOptions
        {
            ConflictPolicy = ERebindConflictPolicy.Fail,
            ExcludeMouse = true,
        };
    }

    /// <summary>
    /// 动作上某个绑定的只读快照（设备路径 + 可选复合部件名）。
    /// </summary>
    public readonly struct InputBindingData : IEquatable<InputBindingData>
    {
        /// <summary>控件设备路径（如 <c>&lt;Keyboard&gt;/space</c>）。</summary>
        public readonly string Path;

        /// <summary>复合绑定的部件名（非复合绑定与整体快照为 null）。</summary>
        public readonly string PartName;

        /// <summary>该绑定是否可被用户覆盖（<see cref="InputBinding"/> 的 override 状态在此不反映）。</summary>
        public readonly bool IsComposite;

        /// <summary>
        /// 创建绑定快照。
        /// </summary>
        public InputBindingData(string path, string partName = null, bool isComposite = false)
        {
            Path = path;
            PartName = partName;
            IsComposite = isComposite;
        }

        public bool Equals(InputBindingData other) =>
            string.Equals(Path, other.Path, StringComparison.Ordinal) &&
            string.Equals(PartName, other.PartName, StringComparison.Ordinal) &&
            IsComposite == other.IsComposite;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is InputBindingData other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Path != null ? Path.GetHashCode() : 0;
                hash = (hash * 397) ^ (PartName != null ? PartName.GetHashCode() : 0);
                hash = (hash * 397) ^ IsComposite.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// 输入重绑定能力接口：仅支持运行期改绑的后端实现（当前为 Input System 后端）。
    /// </summary>
    /// <remarks>
    /// 能力面按窄接口探测（<c>InputService.Rebinding</c>），不并入 <see cref="IInputService"/> 主契约—— <br />
    /// 旧 InputManager 与移动端 UI 后端无运行期改绑概念，不被迫空实现。 <br />
    /// 覆写按动作名寻址，落绑与恢复均即时生效；持久化由调用方经 <see cref="GameSettings"/> 键值自行承载。
    /// </remarks>
    public interface IInputRebindable
    {
        /// <summary>
        /// 交互式重绑：开始监听下一个用户输入并把其控件路径落到目标动作的绑定上。
        /// </summary>
        /// <param name="actionName">目标动作名（与查询 API 同寻址口径）。</param>
        /// <param name="actionGroup">目标动作分组（空串为全局）。</param>
        /// <param name="options">重绑选项（冲突策略/屏蔽鼠标触屏/复合部件定位）。</param>
        /// <param name="cancelToken">取消令牌：触发即结束监听并取消本次重绑。</param>
        /// <returns>终态快照（成功/取消/冲突/不可重绑）。</returns>
        UniTask<InputRebindResult> RebindAsync(string actionName, string actionGroup,
            InputRebindOptions options, System.Threading.CancellationToken cancelToken);

        /// <summary>
        /// 读取动作当前生效的绑定覆写快照（无覆写返回 false）。
        /// </summary>
        bool TryGetBindingOverride(string actionName, string actionGroup, out InputBindingData data);

        /// <summary>
        /// 应用绑定覆写（读档/恢复用）；path 为 null 或空移除覆写回到默认。
        /// </summary>
        void ApplyBindingOverride(string actionName, string actionGroup, in InputBindingData data);

        /// <summary>
        /// 移除动作的全部覆写，回到资产默认绑定。
        /// </summary>
        void ResetBinding(string actionName, string actionGroup);

        /// <summary>
        /// 移除全部动作的覆写。
        /// </summary>
        void ResetAllBindings();
    }
}
