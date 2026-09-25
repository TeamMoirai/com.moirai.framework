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
        /// 多语言表按语言分份导出后，各语言子目录下的同名数据文件名。
        /// <remarks>子目录名即语言码，与 <see cref="L10nLanguages.Codes"/> 同源，由转表脚本决定。</remarks>
        /// </summary>
        private const string LOCALIZED_STRINGS_TABLE = "l10n_tblocalizedstrings";

        /// <summary>
        /// 词条按语言各存一份，走框架的按语言列模式：常驻与取值都只有语言头 + 当前语言列。
        /// </summary>
        public override bool SupportsPerLanguageLocalizationLoad => true;

        /// <summary>
        /// 自报本表提供的语言：顺序即 <see cref="GetLocalizedStringsByLanguage"/> 各列在框架内的列序，
        /// 也是 <see cref="GetAllLocalizedStrings"/> 里每条形文本的列顺序。
        /// <para>语言由转表期生成的 <see cref="L10nLanguages"/> 登记，而不是从 bean 字段名反推：
        /// 多语言改走字段变体后 bean 只剩一个 text 字段，字段名与语言无关。</para>
        /// </summary>
        public override IReadOnlyList<string> GetLocalizationLanguageCodes() => L10nLanguages.Codes;

        /// <summary>
        /// 取一种语言的词条列：读该语言子目录下的那份数据。
        /// </summary>
        /// <param name="languageCode">必须是 <see cref="L10nLanguages.Codes"/> 里的语言码。</param>
        /// <returns>未登记的语言返回 <c>null</c>（框架按「未就绪」保持重试）。</returns>
        public override Dictionary<string, string> GetLocalizedStringsByLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) return null;
            if (Array.IndexOf(L10nLanguages.Codes, languageCode) < 0) return null;

            return ReadLanguageColumn(languageCode);
        }

        /// <summary>
        /// 整批结果：逐语言各读一份再按 <see cref="L10nLanguages.Codes"/> 的顺序拼列。
        /// <para>按语言列模式下运行期不会走到这里，留给编辑器预览——预览要同时看到所有语言。</para>
        /// </summary>
        private Dictionary<string, List<string>> _allLocalizedStrings;

        public override Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            if (_allLocalizedStrings == null)
            {
                _allLocalizedStrings = BuildAllLocalizedStrings();
            }

            return _allLocalizedStrings;
        }

        /// <summary>
        /// 逐语言装载并校验列对齐。
        /// <remarks>各语言的数据由各自那一趟导出产生，任一趟失败都会留下缺语言目录；
        /// 缺一列会让后续键整体错位，而框架侧只以「列数与语言数不符」整批拒收，
        /// 所以在这里点名是哪一种语言缺行。</remarks>
        /// </summary>
        private Dictionary<string, List<string>> BuildAllLocalizedStrings()
        {
            var codes = L10nLanguages.Codes;
            var localizedStrings = new Dictionary<string, List<string>>();

            for (int i = 0; i < codes.Length; i++)
            {
                var column = ReadLanguageColumn(codes[i]);

                foreach (var pair in column)
                {
                    if (localizedStrings.TryGetValue(pair.Key, out var columns))
                    {
                        columns.Add(pair.Value);
                    }
                    else
                    {
                        // 每种语言那份数据都含全部键（缺译是空串而不是省键），所以第 i 列的行数即已装载的语言数
                        localizedStrings.Add(pair.Key, new List<string>(codes.Length) { pair.Value });
                    }
                }

                // 每种语言那份数据都含全部键（缺译是空串而不是省键），所以第 i 趟之后每个键都该正好 i+1 列。
                // 少一列说明这一语言漏了键，继续拼只会让后面的列整体错位，而框架侧只以「列数与语言数不符」整批拒收
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
        /// 按语言子目录装载一份多语言表并压平成 key → 译文。
        /// </summary>
        private Dictionary<string, string> ReadLanguageColumn(string languageCode)
        {
            LogUtility.Info("<color=yellow>▼▼▼ Start Load Localization Column[{0}] ▼▼▼</color>",
                languageCode);

            // Tables 里没有多语言表：它按语言分份，逐语言自建，不占启动期的整表展开。
            // 走 LoadTable 而不是直接 new：bin 与 json 两条路线的构造器收的缓冲类型不同，
            // 由生成代码自己决定，换路线不必改这里。
            var table = LoadTable<TbLocalizedStrings>(languageCode + "/" + LOCALIZED_STRINGS_TABLE);

            var column = new Dictionary<string, string>(table.DataList.Count);
            foreach (var data in table.DataList)
            {
                column[data.Key] = data.FormattedStrings.Text;
            }

            LogUtility.Info("<color=yellow>▲▲▲ Localization Column[{0}] Loaded: {1} entries ▲▲▲</color>",
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