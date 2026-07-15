using System;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    [Serializable]
    public class Language : IEquatable<Language>
    {
        /// <summary>
        /// 参考自 <see cref="UnityEngine.SystemLanguage"/>
        /// </summary>
        /// <remarks>保留 Unspecified 作为默认</remarks>
        public static Language[] BuiltinLanguages
        {
            get
            {
                return new[]
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
            }
        }

        /// <summary>
        /// 南非荷兰语
        /// </summary>
        public static Language Afrikaans => new Language(
            nameof(SystemLanguage.Afrikaans), "af", false, "Afrikaans");

        /// <summary>
        /// 阿拉伯语
        /// </summary>
        public static Language Arabic => new Language(
            nameof(SystemLanguage.Arabic), "ar", false, "العربية");

        /// <summary>
        /// 巴斯克语
        /// </summary>
        public static Language Basque => new Language(
            nameof(SystemLanguage.Basque), "eu", false, "Euskara");

        /// <summary>
        /// 白俄罗斯语
        /// </summary>
        public static Language Belarusian => new Language(
            nameof(SystemLanguage.Belarusian), "be", false, "Беларуская");

        /// <summary>
        /// 保加利亚语
        /// </summary>
        public static Language Bulgarian => new Language(
            nameof(SystemLanguage.Bulgarian), "bg", false, "Български");

        /// <summary>
        /// 加泰罗尼亚语
        /// </summary>
        public static Language Catalan => new Language(
            nameof(SystemLanguage.Catalan), "ca", false, "Català");

        /// <summary>
        /// 中文
        /// </summary>
        public static Language Chinese => new Language(
            nameof(SystemLanguage.Chinese), "zh", false, "中文");

        /// <summary>
        /// 捷克语
        /// </summary>
        public static Language Czech => new Language(
            nameof(SystemLanguage.Czech), "cs", false, "Čeština");

        /// <summary>
        /// 丹麦语
        /// </summary>
        public static Language Danish => new Language(
            nameof(SystemLanguage.Danish), "da", false, "Dansk");

        /// <summary>
        /// 荷兰语
        /// </summary>
        public static Language Dutch => new Language(
            nameof(SystemLanguage.Dutch), "nl", false, "Nederlands");

        /// <summary>
        /// 英语
        /// </summary>
        public static Language English => new Language(
            nameof(SystemLanguage.English), "en", false, "English");

        /// <summary>
        /// 爱沙尼亚语
        /// </summary>
        public static Language Estonian => new Language(
            nameof(SystemLanguage.Estonian), "et", false, "Eesti");

        /// <summary>
        /// 法罗语
        /// </summary>
        public static Language Faroese => new Language(
            nameof(SystemLanguage.Faroese), "fo", false, "Føroyskt");

        /// <summary>
        /// 芬兰语
        /// </summary>
        public static Language Finnish => new Language(
            nameof(SystemLanguage.Finnish), "fi", false, "Suomi");

        /// <summary>
        /// 法语
        /// </summary>
        public static Language French => new Language(
            nameof(SystemLanguage.French), "fr", false, "Français");

        /// <summary>
        /// 德语
        /// </summary>
        public static Language German => new Language(
            nameof(SystemLanguage.German), "de", false, "Deutsch");

        /// <summary>
        /// 希腊语
        /// </summary>
        public static Language Greek => new Language(
            nameof(SystemLanguage.Greek), "el", false, "Ελληνικά");

        /// <summary>
        /// 希伯来语
        /// </summary>
        public static Language Hebrew => new Language(
            nameof(SystemLanguage.Hebrew), "he", false, "עברית");

        /// <summary>
        /// 匈牙利语
        /// </summary>
        public static Language Hungarian => new Language(
            SystemLanguage.Hungarian.ToString(), "hu", false, "Magyar");

        /// <summary>
        /// 冰岛语
        /// </summary>
        public static Language Icelandic => new Language(
            nameof(SystemLanguage.Icelandic), "is", false, "Íslenska");

        /// <summary>
        /// 印度尼西亚语
        /// </summary>
        public static Language Indonesian => new Language(
            nameof(SystemLanguage.Indonesian), "id", false, "Bahasa Indonesia");

        /// <summary>
        /// 意大利语
        /// </summary>
        public static Language Italian => new Language(
            nameof(SystemLanguage.Italian), "it", false, "Italiano");

        /// <summary>
        /// 日语
        /// </summary>
        public static Language Japanese => new Language(
            nameof(SystemLanguage.Japanese), "ja", false, "日本語");

        /// <summary>
        /// 韩语
        /// </summary>
        public static Language Korean => new Language(
            nameof(SystemLanguage.Korean), "ko", false, "한국어");

        /// <summary>
        /// 拉脱维亚语
        /// </summary>
        public static Language Latvian => new Language(
            nameof(SystemLanguage.Latvian), "lv", false, "Latviešu");

        /// <summary>
        /// 立陶宛语
        /// </summary>
        public static Language Lithuanian => new Language(
            nameof(SystemLanguage.Lithuanian), "lt", false, "Lietuvių");

        /// <summary>
        /// 挪威语
        /// </summary>
        public static Language Norwegian => new Language(
            nameof(SystemLanguage.Norwegian), "no", false, "Norsk");

        /// <summary>
        /// 波兰语
        /// </summary>
        public static Language Polish => new Language(
            nameof(SystemLanguage.Polish), "pl", false, "Polski");

        /// <summary>
        /// 葡萄牙语
        /// </summary>
        public static Language Portuguese => new Language(
            nameof(SystemLanguage.Portuguese), "pt", false, "Português");

        /// <summary>
        /// 罗马尼亚语
        /// </summary>
        public static Language Romanian => new Language(
            nameof(SystemLanguage.Romanian), "ro", false, "Română");

        /// <summary>
        /// 俄语
        /// </summary>
        public static Language Russian => new Language(
            nameof(SystemLanguage.Russian), "ru", false, "Русский");

        /// <summary>
        /// 塞尔维亚克罗地亚语
        /// </summary>
        public static Language SerboCroatian => new Language(
            nameof(SystemLanguage.SerboCroatian), "hr", false, "Hrvatski");

        /// <summary>
        /// 斯洛伐克语
        /// </summary>
        public static Language Slovak => new Language(
            nameof(SystemLanguage.Slovak), "sk", false, "Slovenčina");

        /// <summary>
        /// 斯洛文尼亚语
        /// </summary>
        public static Language Slovenian => new Language(
            nameof(SystemLanguage.Slovenian), "sl", false, "Slovenščina");

        /// <summary>
        /// 西班牙语
        /// </summary>
        public static Language Spanish => new Language(
            nameof(SystemLanguage.Spanish), "es", false, "Español");

        /// <summary>
        /// 瑞典语
        /// </summary>
        public static Language Swedish => new Language(
            nameof(SystemLanguage.Swedish), "sv", false, "Svenska");

        /// <summary>
        /// 泰语
        /// </summary>
        public static Language Thai => new Language(
            nameof(SystemLanguage.Thai), "th", false, "ไทย");

        /// <summary>
        /// 土耳其语
        /// </summary>
        public static Language Turkish => new Language(
            nameof(SystemLanguage.Turkish), "tr", false, "Türkçe");

        /// <summary>
        /// 乌克兰语
        /// </summary>
        public static Language Ukrainian => new Language(
            nameof(SystemLanguage.Ukrainian), "uk", false, "Українська");

        /// <summary>
        /// 越南语
        /// </summary>
        public static Language Vietnamese => new Language(
            nameof(SystemLanguage.Vietnamese), "vi", false, "Tiếng Việt");

        /// <summary>
        /// 简体中文
        /// </summary>
        public static Language ChineseSimplified => new Language(
            nameof(SystemLanguage.ChineseSimplified), "zh-Hans", false, "简体中文");

        /// <summary>
        /// 繁体中文
        /// </summary>
        public static Language ChineseTraditional => new Language(
            nameof(SystemLanguage.ChineseTraditional), "zh-Hant", false, "繁體中文");

        /// <summary>
        /// 印地语
        /// </summary>
        public static Language Hindi => new Language(
            nameof(SystemLanguage.Hindi), "hi", false, "हिन्दी");

        /// <summary>
        /// 未指定
        /// </summary>
        public static Language Unspecified => new Language(
            "Unspecified", "und", false, "Unspecified");


        #region 语言定义
        
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
            var index = Array.FindIndex(BuiltinLanguages, x => x.Name == systemLanguage.ToString());
            return index >= 0 ? BuiltinLanguages[index] : Unspecified;
        }

        public static explicit operator SystemLanguage(Language language)
        {
            if (language.Custom) return SystemLanguage.Unknown;

            var systemLanguages = (SystemLanguage[]) Enum.GetValues(typeof(SystemLanguage));
            var index = Array.FindIndex(systemLanguages, x => x.ToString() == language.Name);
            return index >= 0 ? systemLanguages[index] : SystemLanguage.Unknown;
        }
        
        #endregion
    }
}