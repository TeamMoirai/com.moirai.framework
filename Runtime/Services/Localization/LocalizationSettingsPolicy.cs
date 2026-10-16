namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 语言设置政策：存档键名与读写口径在此定义一次，写入时机由 <see cref="LocalizationService"/> 的显式切换入口决定。
    /// </summary>
    /// <remarks>
    /// 本地化处理器只切换语言，不再自己决定要不要记住用户的选择；
    /// 首启自动解析（检测链给的语言）因此没有写入点，也就不会被固化进存档。
    /// </remarks>
    internal static class LocalizationSettingsPolicy
    {
        /// <summary>写语言存档。</summary>
        /// <param name="code">语言 Code（如 <c>zh-Hans</c>），不是语言 Name。</param>
        internal static void Save(string code) => SettingUtility.SetString(GameConstant.Setting.LANGUAGE, code);

        /// <summary>读语言存档。</summary>
        /// <param name="code">已存的语言 Code；没写过或写过空值时为 null。</param>
        /// <returns>存档里确实有一门语言时返回 <c>true</c>。</returns>
        internal static bool TryLoad(out string code)
        {
            code = null;
            if (!SettingUtility.HasSetting(GameConstant.Setting.LANGUAGE)) return false;

            code = SettingUtility.GetString(GameConstant.Setting.LANGUAGE);
            return !string.IsNullOrEmpty(code);
        }

        /// <summary>抹掉语言存档，下一次启动回到检测链。</summary>
        internal static void Remove() => SettingUtility.RemoveSetting(GameConstant.Setting.LANGUAGE);
    }
}
