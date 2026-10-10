using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 构建期渠道默认语言的烘焙产物（由 <c>LocalizationBuildBaker</c> 出包时写入，按渠道分发到 <c>Resources/</c>）。
    /// </summary>
    /// <remarks>
    /// 语言检测链中位于「本地存档」之后、「系统语言」之前：多渠道出包各带默认语言，玩家改语言后本地存档仍然优先。 <br />
    /// 编辑器与播放预览不消费本资产。
    /// </remarks>
    public sealed class LocalizationBuildConfig : ScriptableObject
    {
        [SerializeField] private string m_LanguageCode;

        /// <summary>渠道默认语言的 Name 或 Code（与 <c>LocalizationService.ToLanguage</c> 同一解析面）。</summary>
        public string LanguageCode => m_LanguageCode;

        /// <summary>
        /// 写入烘焙值（仅编辑器烘焙路径调用）。
        /// </summary>
        internal void SetLanguageCode(string languageCode) => m_LanguageCode = languageCode;
    }
}
