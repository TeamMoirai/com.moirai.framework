namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入重绑定持久化政策：设置键拼装、绑定快照的编码与解码在此定义一次，后端与设置页都从这里取。
    /// </summary>
    /// <remarks>
    /// 覆写值编码为「控件路径[|部件名]」单串（复合部件覆写保留部件定位），存经 <see cref="GameSettings"/> 字符串键值； <br />
    /// 读取即应用：动作在初始化装载后按已存键恢复覆写，未写入过时无键即无覆写。
    /// </remarks>
    internal static class InputRebindSettingsPolicy
    {
        /// <summary>绑定快照的路径与部件名分隔符（部件名含 | 的绑定不参与编码，见 <see cref="Encode"/>）。</summary>
        private const char SEPARATOR = '|';

        /// <summary>指定动作的重绑定设置键。</summary>
        /// <param name="fullActionName">动作全限定名（Group/Name）。</param>
        internal static string BindingKey(string fullActionName) =>
            StringUtility.Format(GameConstant.Setting.INPUT_REBIND_BINDING, fullActionName);

        /// <summary>移除动作的重绑定设置（移除即回到资产默认绑定）。</summary>
        /// <param name="fullActionName">动作全限定名（Group/Name）。</param>
        internal static void RemoveBinding(string fullActionName) =>
            GameSettings.RemoveSetting(BindingKey(fullActionName));

        /// <summary>
        /// 把绑定快照编码为单串设置值。
        /// </summary>
        /// <returns>编码值；无法编码（部件名含分隔符）返回 null 不落盘。</returns>
        internal static string Encode(in InputBindingData data)
        {
            if (string.IsNullOrEmpty(data.Path)) return null;

            if (!string.IsNullOrEmpty(data.PartName))
            {
                // 部件名含分隔符会破坏解码，按不支持处理（复合部件名来自资产定义，实际不出现）
                if (data.PartName.IndexOf(SEPARATOR) >= 0) return null;
                return string.Concat(data.Path, "|", data.PartName);
            }

            return data.Path;
        }

        /// <summary>
        /// 把设置值解码为绑定快照。
        /// </summary>
        /// <returns>解码成功为 true；无效值（空/多段）返回 false。</returns>
        internal static bool TryDecode(string value, out InputBindingData data)
        {
            data = default;
            if (string.IsNullOrEmpty(value)) return false;

            int separator = value.IndexOf(SEPARATOR);
            if (separator < 0)
            {
                data = new InputBindingData(value);
                return true;
            }

            // 多个分隔符为无效值（部件名不允许 |）
            if (value.IndexOf(SEPARATOR, separator + 1) >= 0) return false;

            data = new InputBindingData(value.Substring(0, separator), value.Substring(separator + 1));
            return true;
        }
    }
}
