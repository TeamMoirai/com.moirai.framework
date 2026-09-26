using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    [Serializable]
    public class Language : IEquatable<Language>
    {
        private static readonly Dictionary<SystemLanguage, Language> s_FromSystemLanguage;
        private static readonly Dictionary<string, SystemLanguage> s_ToSystemLanguage;

        private static readonly Language[] s_BuiltinLanguages;
        /// <summary>
        /// 参考自 <see cref="UnityEngine.SystemLanguage"/>
        /// </summary>
        /// <remarks>
        /// 保留 Unspecified 作为默认。
        /// <para>返回共享实例而非每次新建：语言表在检测链与查询路径上高频访问，
        /// 逐次重建会产生约 45 个对象的分配。作为代价，<b>调用方禁止原地改写元素或长度</b>，
        /// 否则会污染全局语言表。</para>
        /// </remarks>
        public static Language[] BuiltinLanguages => s_BuiltinLanguages;

        /// <summary>
        /// 南非荷兰语
        /// </summary>
        public static Language Afrikaans { get; } = new Language(
            nameof(SystemLanguage.Afrikaans), "af", false, "Afrikaans");

        /// <summary>
        /// 阿拉伯语
        /// </summary>
        public static Language Arabic { get; } = new Language(
            nameof(SystemLanguage.Arabic), "ar", false, "العربية");

        /// <summary>
        /// 巴斯克语
        /// </summary>
        public static Language Basque { get; } = new Language(
            nameof(SystemLanguage.Basque), "eu", false, "Euskara");

        /// <summary>
        /// 白俄罗斯语
        /// </summary>
        public static Language Belarusian { get; } = new Language(
            nameof(SystemLanguage.Belarusian), "be", false, "Беларуская");

        /// <summary>
        /// 保加利亚语
        /// </summary>
        public static Language Bulgarian { get; } = new Language(
            nameof(SystemLanguage.Bulgarian), "bg", false, "Български");

        /// <summary>
        /// 加泰罗尼亚语
        /// </summary>
        public static Language Catalan { get; } = new Language(
            nameof(SystemLanguage.Catalan), "ca", false, "Català");

        /// <summary>
        /// 中文
        /// </summary>
        public static Language Chinese { get; } = new Language(
            nameof(SystemLanguage.Chinese), "zh", false, "中文");

        /// <summary>
        /// 捷克语
        /// </summary>
        public static Language Czech { get; } = new Language(
            nameof(SystemLanguage.Czech), "cs", false, "Čeština");

        /// <summary>
        /// 丹麦语
        /// </summary>
        public static Language Danish { get; } = new Language(
            nameof(SystemLanguage.Danish), "da", false, "Dansk");

        /// <summary>
        /// 荷兰语
        /// </summary>
        public static Language Dutch { get; } = new Language(
            nameof(SystemLanguage.Dutch), "nl", false, "Nederlands");

        /// <summary>
        /// 英语
        /// </summary>
        public static Language English { get; } = new Language(
            nameof(SystemLanguage.English), "en", false, "English");

        /// <summary>
        /// 爱沙尼亚语
        /// </summary>
        public static Language Estonian { get; } = new Language(
            nameof(SystemLanguage.Estonian), "et", false, "Eesti");

        /// <summary>
        /// 法罗语
        /// </summary>
        public static Language Faroese { get; } = new Language(
            nameof(SystemLanguage.Faroese), "fo", false, "Føroyskt");

        /// <summary>
        /// 芬兰语
        /// </summary>
        public static Language Finnish { get; } = new Language(
            nameof(SystemLanguage.Finnish), "fi", false, "Suomi");

        /// <summary>
        /// 法语
        /// </summary>
        public static Language French { get; } = new Language(
            nameof(SystemLanguage.French), "fr", false, "Français");

        /// <summary>
        /// 德语
        /// </summary>
        public static Language German { get; } = new Language(
            nameof(SystemLanguage.German), "de", false, "Deutsch");

        /// <summary>
        /// 希腊语
        /// </summary>
        public static Language Greek { get; } = new Language(
            nameof(SystemLanguage.Greek), "el", false, "Ελληνικά");

        /// <summary>
        /// 希伯来语
        /// </summary>
        public static Language Hebrew { get; } = new Language(
            nameof(SystemLanguage.Hebrew), "he", false, "עברית");

        /// <summary>
        /// 匈牙利语
        /// </summary>
        public static Language Hungarian { get; } = new Language(
            SystemLanguage.Hungarian.ToString(), "hu", false, "Magyar");

        /// <summary>
        /// 冰岛语
        /// </summary>
        public static Language Icelandic { get; } = new Language(
            nameof(SystemLanguage.Icelandic), "is", false, "Íslenska");

        /// <summary>
        /// 印度尼西亚语
        /// </summary>
        public static Language Indonesian { get; } = new Language(
            nameof(SystemLanguage.Indonesian), "id", false, "Bahasa Indonesia");

        /// <summary>
        /// 意大利语
        /// </summary>
        public static Language Italian { get; } = new Language(
            nameof(SystemLanguage.Italian), "it", false, "Italiano");

        /// <summary>
        /// 日语
        /// </summary>
        public static Language Japanese { get; } = new Language(
            nameof(SystemLanguage.Japanese), "ja", false, "日本語");

        /// <summary>
        /// 韩语
        /// </summary>
        public static Language Korean { get; } = new Language(
            nameof(SystemLanguage.Korean), "ko", false, "한국어");

        /// <summary>
        /// 拉脱维亚语
        /// </summary>
        public static Language Latvian { get; } = new Language(
            nameof(SystemLanguage.Latvian), "lv", false, "Latviešu");

        /// <summary>
        /// 立陶宛语
        /// </summary>
        public static Language Lithuanian { get; } = new Language(
            nameof(SystemLanguage.Lithuanian), "lt", false, "Lietuvių");

        /// <summary>
        /// 挪威语
        /// </summary>
        public static Language Norwegian { get; } = new Language(
            nameof(SystemLanguage.Norwegian), "no", false, "Norsk");

        /// <summary>
        /// 波兰语
        /// </summary>
        public static Language Polish { get; } = new Language(
            nameof(SystemLanguage.Polish), "pl", false, "Polski");

        /// <summary>
        /// 葡萄牙语
        /// </summary>
        public static Language Portuguese { get; } = new Language(
            nameof(SystemLanguage.Portuguese), "pt", false, "Português");

        /// <summary>
        /// 罗马尼亚语
        /// </summary>
        public static Language Romanian { get; } = new Language(
            nameof(SystemLanguage.Romanian), "ro", false, "Română");

        /// <summary>
        /// 俄语
        /// </summary>
        public static Language Russian { get; } = new Language(
            nameof(SystemLanguage.Russian), "ru", false, "Русский");

        /// <summary>
        /// 塞尔维亚克罗地亚语
        /// </summary>
        public static Language SerboCroatian { get; } = new Language(
            nameof(SystemLanguage.SerboCroatian), "hr", false, "Hrvatski");

        /// <summary>
        /// 斯洛伐克语
        /// </summary>
        public static Language Slovak { get; } = new Language(
            nameof(SystemLanguage.Slovak), "sk", false, "Slovenčina");

        /// <summary>
        /// 斯洛文尼亚语
        /// </summary>
        public static Language Slovenian { get; } = new Language(
            nameof(SystemLanguage.Slovenian), "sl", false, "Slovenščina");

        /// <summary>
        /// 西班牙语
        /// </summary>
        public static Language Spanish { get; } = new Language(
            nameof(SystemLanguage.Spanish), "es", false, "Español");

        /// <summary>
        /// 瑞典语
        /// </summary>
        public static Language Swedish { get; } = new Language(
            nameof(SystemLanguage.Swedish), "sv", false, "Svenska");

        /// <summary>
        /// 泰语
        /// </summary>
        public static Language Thai { get; } = new Language(
            nameof(SystemLanguage.Thai), "th", false, "ไทย");

        /// <summary>
        /// 土耳其语
        /// </summary>
        public static Language Turkish { get; } = new Language(
            nameof(SystemLanguage.Turkish), "tr", false, "Türkçe");

        /// <summary>
        /// 乌克兰语
        /// </summary>
        public static Language Ukrainian { get; } = new Language(
            nameof(SystemLanguage.Ukrainian), "uk", false, "Українська");

        /// <summary>
        /// 越南语
        /// </summary>
        public static Language Vietnamese { get; } = new Language(
            nameof(SystemLanguage.Vietnamese), "vi", false, "Tiếng Việt");

        /// <summary>
        /// 简体中文
        /// </summary>
        public static Language ChineseSimplified { get; } = new Language(
            nameof(SystemLanguage.ChineseSimplified), "zh-Hans", false, "简体中文");

        /// <summary>
        /// 繁体中文
        /// </summary>
        public static Language ChineseTraditional { get; } = new Language(
            nameof(SystemLanguage.ChineseTraditional), "zh-Hant", false, "繁體中文");

        /// <summary>
        /// 印地语
        /// </summary>
        public static Language Hindi { get; } = new Language(
            nameof(SystemLanguage.Hindi), "hi", false, "हिन्दी");

        /// <summary>
        /// 未指定
        /// </summary>
        public static Language Unspecified { get; } = new Language(
            "Unspecified", "und", false, "Unspecified");


        #region 语言定义 [LANGUAGE DEFINITIONS]
        
        [SerializeField] private string m_Name;

        [SerializeField] private string m_Code;

        [SerializeField] private bool m_Custom;

        [SerializeField] private string m_DisplayName;

        /// <summary>
        /// 语言名称。
        /// </summary>
        /// <remarks>系统语言下的枚举(英文)</remarks>
        public string Name => m_Name;

        /// <summary>
        /// 获取 <see href="https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes">ISO-639-1</see> 语言代码。
        /// </summary>
        /// <returns>ISO-639-1 code.</returns>
        public string Code => m_Code;

        /// <summary>
        /// 语言是自定义的还是内置的，支持 <see cref="SystemLanguage"/> 转换.
        /// </summary>
        public bool Custom => m_Custom;

        /// <summary>
        /// 语言显示名称。
        /// </summary>
        /// <example>English => English、ChineseSimplified => 简体中文、ChineseTraditional => 繁體中文</example>
        public string DisplayName => !string.IsNullOrEmpty(m_DisplayName) ? m_DisplayName : m_Name;

        // 判 RTL 的 Code 白名单：只列 ar 与 he 两枚，不从语族或文字系统推断——
        // 同属阿拉伯文字系统的波斯语、乌尔都语不在名单内，要进名单就改这里并连用例与文档一起对齐。
        private static readonly HashSet<string> s_RtlCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Arabic.Code, Hebrew.Code,
        };

        /// <summary>
        /// 该语言是否从右向左书写（阿拉伯语 <c>ar</c>、希伯来语 <c>he</c>）。
        /// </summary>
        /// <remarks>仅 TMP 注入应用（<c>TMP_Text.isRightToLeftText</c>）；UGUI Text 与 TextMesh 无 RTL 排版能力。</remarks>
        public bool IsRightToLeft => s_RtlCodes.Contains(m_Code);

        public Language(string name, string code)
        {
            m_Name = name ?? "";
            m_Code = code ?? "";
            m_Custom = true;
            m_DisplayName = "";
        }

        public Language(Language other)
        {
            m_Name = other.m_Name;
            m_Code = other.m_Code;
            m_Custom = other.m_Custom;
            m_DisplayName = other.m_DisplayName;
        }

        internal Language(string name, string code, bool custom, string displayName = "")
        {
            m_Name = name ?? "";
            m_Code = code ?? "";
            m_Custom = custom;
            m_DisplayName = displayName ?? "";
        }

        public bool Equals(Language other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Code == other.Code;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Language) obj);
        }

        public override int GetHashCode()
        {
            return Code.GetHashCode();
        }

        public static bool operator ==(Language left, Language right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Language left, Language right)
        {
            return !Equals(left, right);
        }

        public override string ToString()
        {
            return Name;
        }

        public static implicit operator Language(SystemLanguage systemLanguage)
        {
            return s_FromSystemLanguage.GetValueOrDefault(systemLanguage, Unspecified);
        }

        public static explicit operator SystemLanguage(Language language)
        {
            if (language == null || language.Custom) return SystemLanguage.Unknown;
            return s_ToSystemLanguage.GetValueOrDefault(language.Name, SystemLanguage.Unknown);
        }

        static Language()
        {
            s_BuiltinLanguages = new[]
            {
                Unspecified,
                Afrikaans,
                Arabic,
                Basque,
                Belarusian,
                Bulgarian,
                Catalan,
                Chinese,
                Czech,
                Danish,
                Dutch,
                English,
                Estonian,
                Faroese,
                Finnish,
                French,
                German,
                Greek,
                Hebrew,
                Hungarian,
                Icelandic,
                Indonesian,
                Italian,
                Japanese,
                Korean,
                Latvian,
                Lithuanian,
                Norwegian,
                Polish,
                Portuguese,
                Romanian,
                Russian,
                SerboCroatian,
                Slovak,
                Slovenian,
                Spanish,
                Swedish,
                Thai,
                Turkish,
                Ukrainian,
                Vietnamese,
                ChineseSimplified,
                ChineseTraditional,
                Hindi,
            };

            // SystemLanguage 双向映射：语言检测链每次访问都要解析，
            // 用 Array.FindIndex(BuiltinLanguages, ...) 配合原先逐次 new 的语言表，
            // 一次转换即产生整张表（约 45 个对象）的分配。
            var builtinByName = new Dictionary<string, Language>(s_BuiltinLanguages.Length, StringComparer.Ordinal);
            foreach (var language in s_BuiltinLanguages)
            {
                builtinByName[language.Name] = language;
            }

            var systemLanguages = (SystemLanguage[]) Enum.GetValues(typeof(SystemLanguage));
            s_FromSystemLanguage = new Dictionary<SystemLanguage, Language>(systemLanguages.Length);
            s_ToSystemLanguage = new Dictionary<string, SystemLanguage>(systemLanguages.Length, StringComparer.Ordinal);
            foreach (var systemLanguage in systemLanguages)
            {
                // 名称对不上内置语言、或命中的是自定义条目时不参与转换
                if (!builtinByName.TryGetValue(systemLanguage.ToString(), out var language) || language.Custom) continue;

                s_FromSystemLanguage[systemLanguage] = language;
                s_ToSystemLanguage[language.Name] = systemLanguage;
            }
        }

        #endregion
    }
}