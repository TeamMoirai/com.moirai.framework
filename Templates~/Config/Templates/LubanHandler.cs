using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.ConfigTable;
using Moirai.GameProto.Config.L10n;
using UnityEngine;
using UnityEngine.U2D;
using Moirai.Atropos.Resource;

namespace Moirai.GameProto.Config
{
    /// <summary>
    /// 游戏配置表助手。
    /// </summary>
    [Serializable]
	public sealed partial class LubanHandler : ConfigTableServiceHandler
    {
        #region 初始化 [INITIALIZE]

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnDidReloadScripts()
        {
            ConfigTableServiceSettings.InjectConfigTableHandler<LubanHandler>();
        }
#endif

        #endregion

        #region 处理多语言 [LOCALIZATION]

        /// <summary>
        /// 多语言表按语言分份导出后，各语言子目录下的那份词条数据文件名（相对配置根目录、不含扩展名）。
        /// </summary>
        private const string LOCALIZED_STRINGS_TABLE = "l10n_tblocalizedstrings";

        /// <summary>
        /// 词条按语言各存一份，走框架的按语言列模式：常驻与取值都只有语言头 + 当前语言列。
        /// </summary>
        public override bool SupportsPerLanguageLocalizationLoad => true;

        /// <summary>
        /// 自报本表提供的语言：顺序即 <see cref="GetLocalizedStringsByLanguage"/> 各列在框架内的列序，也是 <see cref="GetAllLocalizedStrings"/> 里每条形文本的列顺序。
        /// </summary>
        /// <remarks>
        /// 语言取自转表期生成的 <see cref="L10nLanguages.Codes"/>，不从 bean 字段名反推：多语言改走字段变体后 bean 只剩一个 Text 字段，字段名与语言无关。
        /// </remarks>
        public override IReadOnlyList<string> GetLocalizationLanguageCodes() => L10nLanguages.Codes;

        /// <summary>
        /// 取一种语言的词条列：读该语言子目录下的那份数据。
        /// </summary>
        /// <param name="languageCode"><see cref="GetLocalizationLanguageCodes"/> 自报过的语言码。未登记的返回 <c>null</c>，框架按「数据未就绪」保持重试。</param>
        public override Dictionary<string, string> GetLocalizedStringsByLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;
            if (Array.IndexOf(L10nLanguages.Codes, languageCode) < 0) return null;

            return ReadLanguageColumn(languageCode);
        }

        private Dictionary<string, List<string>> _allLocalizedStrings;

        /// <summary>
        /// 整批结果：逐语言各读一份，再按 <see cref="GetLocalizationLanguageCodes"/> 的顺序拼成每键一行。
        /// </summary>
        /// <remarks>
        /// 按语言列模式下运行期不再走到这里，留给编辑器预览——预览要同时看到所有语言。
        /// </remarks>
        public override Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            if (_allLocalizedStrings == null)
            {
                // 逐语言全部装载成功才落状态：任一趟读表失败都不留下"已解析"的空批，下次访问照常重来
                _allLocalizedStrings = BuildAllLocalizedStrings();
            }

            return _allLocalizedStrings;
        }

        /// <summary>
        /// 逐语言装载并校验列对齐。
        /// </summary>
        /// <remarks>
        /// 每种语言那份数据都含全部键（缺译是空串而不是省键），所以第 i 趟之后每个键都该正好 i+1 列。
        /// 少一列会让后面的列整体错位，而框架侧只以「列数与语言数不符」整批拒收，故在这里点名缺的是哪一种语言。
        /// </remarks>
        private Dictionary<string, List<string>> BuildAllLocalizedStrings()
        {
            var codes = L10nLanguages.Codes;
            var localizedStrings = new Dictionary<string, List<string>>();

            for (int i = 0; i < codes.Length; i++)
            {
                foreach (var pair in ReadLanguageColumn(codes[i]))
                {
                    if (localizedStrings.TryGetValue(pair.Key, out var columns))
                    {
                        columns.Add(pair.Value);
                    }
                    else
                    {
                        localizedStrings.Add(pair.Key, new List<string>(codes.Length) { pair.Value });
                    }
                }

                foreach (var entry in localizedStrings)
                {
                    if (entry.Value.Count != i + 1)
                    {
                        throw new GameException(StringUtility.Format(
                            "Localization data misaligned at language '{0}': key '{1}' has {2} column(s), expected {3}. " +
                            "Re-run config generation so every language in L10N_LANGUAGES has its own '{4}.bytes'.",
                            codes[i], entry.Key, entry.Value.Count, i + 1, LOCALIZED_STRINGS_TABLE));
                    }
                }
            }

            return localizedStrings;
        }

        /// <summary>
        /// 按语言子目录装载一份多语言表，压平成 key → 译文。
        /// </summary>
        private static Dictionary<string, string> ReadLanguageColumn(string languageCode)
        {
            LogUtility.Info("<color=yellow>\u25bc\u25bc\u25bc\u25bc Start Load Localization Column[{0}] \u25bc\u25bc\u25bc\u25bc</color>",
                languageCode);

            // 多语言表不在 Tables 里：它按语言分份导出，逐语言自建，不占启动期的整表展开
            var table = LoadTable<TbLocalizedStrings>(languageCode + "/" + LOCALIZED_STRINGS_TABLE);

            var column = new Dictionary<string, string>(table.DataList.Count);
            foreach (var data in table.DataList)
            {
                column[data.Key] = data.FormattedStrings.Text;
            }

            LogUtility.Info("<color=yellow>\u25b2\u25b2\u25b2\u25b2 Localization Column[{0}] Loaded: {1} entries \u25b2\u25b2\u25b2\u25b2</color>",
                languageCode, column.Count);

            return column;
        }

        #endregion

        #region 界面 [UI]

        public override string GetUIWindowLocation(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (!Tables.TbUIWindow.DataMap.TryGetValue(id, out var uiWindowConfig))
            {
                LogUtility.Warning("UI ID[{0}] is invalid.", id);
                return string.Empty;
            }
            
            // todo 获取当前主题的配置？
            // UI 当前主题配置在 UIConfigManager
            return uiWindowConfig.DefaultRes;
        }

        /// <summary>
        /// 根据图集名（配置表 id 必须为图集名）获取实际 SpriteAtlas
        /// </summary>
        /// <param name="id">UISprite - SpriteAtlas 配置表的 id</param>
        /// <param name="cancellationToken"></param>
#pragma warning disable CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        public override async UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken)
#pragma warning restore CS1998 // 异步方法缺少 "await" 运算符，将以同步方式运行
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (!Tables.TbSprite.DataMap.TryGetValue(id, out var spriteConfig))
            {
                LogUtility.Warning("Sprite ID[{0}] is invalid.", id);
                return null;
            }
            
            if (!Tables.TbSpriteAtlas.DataMap.TryGetValue(spriteConfig.SpriteAtlasId, out var atlasConfig))
            {
                LogUtility.Warning("SpriteAtlasId ID[{0}] is invalid.", id);
                return null;
            }
            
            // LogUtility.Info("LoadSpriteByID {0} from {1}", id, atlasConfig.Location);
            
            using var lease =
#if UNITY_WEBGL
                await ResourceService.LoadLeaseAsync<SpriteAtlas>(atlasConfig.Location, packageName: atlasConfig.PackageName);
#else
                ResourceService.LoadLease<SpriteAtlas>(atlasConfig.Location, packageName:atlasConfig.PackageName);
#endif
            return lease.Asset?.GetSprite(spriteConfig.SpriteName);
        }

        #endregion
    }
}