using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>承载语言管理、语言切换、缺译回退、运行时覆盖与文本查询；词条存储与取值解析落在
    /// <see cref="LocalizationStore"/>，与编辑器预览共用同一套解析路径。</para>
    /// <para>多语言数据在首次访问时一次性全量加载，各语言列随词条常驻内存；按语言拆分懒加载为
    /// 门槛触发项，规模可经 <see cref="ResidentChars"/> 或调试面板量化判断后再实施。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationServiceHandler : FrameworkHandler
    {
        // 缺译回退顺序，用语言 Code 而非 Language 配置：Language 无无参构造，
        // Unity 序列化器无法还原其数组元素。留空即关闭回退（缺译直接返回 key）。
        [Tooltip("缺译回退顺序，填语言 Code（如 en、zh-Hans）。当前语言缺译时按此顺序取译文；留空表示缺译直接返回 key。")]
        [SerializeField] private string[] m_FallbackLanguageCodes = { "en" };

        // 本地化器列表
        [NonSerialized] internal readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();
        // 句柄式订阅表——静态事件那条路上"忘了注销"是唯一没人收口的泄漏，这里在关服时统一作废
        [NonSerialized] private readonly List<LanguageChangeSubscription> _subscriptions = new List<LanguageChangeSubscription>();
        [NonSerialized] private LocalizationStore _store;
        [NonSerialized] private Language _currentLanguage;
        // 当前本地化语言设置来自
        [NonSerialized] private string _settingSource;
        // 本地化数据是否已加载（懒式初始化标记）
        [NonSerialized] private bool _dataLoaded;
        // 数据未就绪期间记录的切换意图（首次加载成功时优先应用，避免启动早期切语言被静默吞掉）
        [NonSerialized] private Language _pendingLanguage;
        // 数据加载失败日志只打一次（数据未就绪时每次查询都会重试加载，避免刷屏）
        [NonSerialized] private bool _hasLoggedLoadError;
        // "无可用语言"日志只打一次（ChangeLanguage 与 Activate 系列共一只闸门，成功加载后复位）
        [NonSerialized] private bool _hasLoggedNoLanguage;
        // 格式化失败日志只打一次（占位符与参数不匹配属表内缺陷，逐条刷屏会淹没日志）
        [NonSerialized] private bool _hasLoggedFormatError;
        // 当前语言在批内的列下标：查询热路径用，省去每次线性扫语言表
        [NonSerialized] private int _currentLanguageIndex = -1;
        // 切换中标记：本地化器注入回调里再切语言会打乱快照与事件顺序，直接拦下
        [NonSerialized] private bool _isSwitching;
        // 回退链解析后的语言与其列下标
        [NonSerialized] private Language[] _fallbackChain = Array.Empty<Language>();
        [NonSerialized] private int[] _fallbackIndices = Array.Empty<int>();
        // 不存在的语言只在切换时警告一次
        [NonSerialized] private HashSet<Language> _warnedUnavailableLanguages;
        // 缺译追踪：全链（覆盖层→指定语言→回退链）都取不到译文的 key，去重记录，供 QA 巡检与调试面板展示
        [NonSerialized] private HashSet<string> _missingKeys;
        // 缺译事件总数（含同一 key 的重复命中）——与去重集合对照可分辨「大面积漏翻」与「高频单点漏翻」
        [NonSerialized] private int _missingKeyEvents;
        // 追踪集合饱和告警只打一次（饱和后计数照走，逐 key 记录与告警停摆，防异常配置刷爆内存与日志）
        [NonSerialized] private bool _hasLoggedMissingCap;
        // 格式化文化：跟随当前游戏语言（数字/日期分隔符与文案语言一致），语言切换时重解析；解析失败回落不变文化
        [NonSerialized] private CultureInfo _formatCulture;

        /// <summary>缺译追踪集合的最大容量。</summary>
        private const int MAX_TRACKED_MISSING_KEYS = 256;

        /// <summary>
        /// 当前使用的本地化语言
        /// </summary>
        public Language CurrentLanguage => _currentLanguage ?? LocalizationService.GetCurrentLanguage(true, ref _settingSource);

        /// <summary>
        /// 当前语言索引（数据未就绪或语言未解析时为 -1）
        /// </summary>
        public int CurrentLanguageIndex
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return _currentLanguageIndex;
            }
        }

        /// <summary>数据是否已加载完成（<c>ToLanguage</c> 的「支持性」判定在未加载时退化为身份解析）。</summary>
        internal bool IsDataLoaded => _dataLoaded;

        /// <summary>语言是否在当前批内（调用方须已确认数据加载完成，见 <see cref="IsDataLoaded"/>）。</summary>
        internal bool IsLanguageAvailable(Language language) => language != null && Store.IndexOf(language) >= 0;

        /// <summary>
        /// 当前批内收录的语言（列序即批内列下标顺序；数据未就绪时为空）。
        /// <para>语言真相源唯一：语言头随批自报，不存在第二份全局注册表。</para>
        /// </summary>
        public IReadOnlyList<Language> LoadedLanguages
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return Store.Batch.Languages;
            }
        }

        /// <summary>
        /// 缺译回退链（不含当前语言，按 <see cref="m_FallbackLanguageCodes"/> 配置顺序）。
        /// <para>访问会触发数据加载；配置了不可用的语言时按序剔除。</para>
        /// </summary>
        public IReadOnlyList<Language> FallbackChain
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return _fallbackChain;
            }
        }

        /// <summary>
        /// 缺译回退链的语言 Code 配置（按回退顺序）。
        /// </summary>
        /// <remarks>用 Code 字符串而非 <see cref="Language"/>：后者无无参构造，Unity 序列化器还原不了数组元素。
        /// 赋值时若数据已加载会立即重解析回退链。</remarks>
        public string[] FallbackLanguageCodes
        {
            get => m_FallbackLanguageCodes;
            set
            {
                m_FallbackLanguageCodes = value;
                if (_dataLoaded) ResolveFallbackChain();
            }
        }

        /// <summary>已加载的词条数（数据未就绪时为 0）</summary>
        public int EntryCount
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return Store.EntryCount;
            }
        }

        /// <summary>已加载的语言数（数据未就绪时为 0）</summary>
        public int LanguageCount
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return Store.LanguageCount;
            }
        }

        /// <summary>
        /// 全部语言列的译文总字符数。
        /// </summary>
        /// <remarks>UTF-16 下每字符 2 字节，是常驻译文的<b>下限</b>估算（不含字符串对象头与字典开销），
        /// 用于判断是否到了必须按语言拆包加载的量级。</remarks>
        public long ResidentChars
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return Store.ResidentChars;
            }
        }

        /// <summary>已记录的去重缺译 key 数（纯诊断读取，不触发数据加载）。</summary>
        public int MissingKeyCount => _missingKeys?.Count ?? 0;

        /// <summary>缺译事件总数，含同一 key 的重复命中（纯诊断读取，不触发数据加载）。</summary>
        public int MissingKeyEventCount => _missingKeyEvents;

        /// <summary>
        /// 取已记录缺译 key 的有序快照（诊断用；不触发数据加载）。
        /// </summary>
        public string[] GetMissingKeys()
        {
            if (_missingKeys == null || _missingKeys.Count == 0) return Array.Empty<string>();

            var keys = new string[_missingKeys.Count];
            _missingKeys.CopyTo(keys);
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// 清空缺译记录（QA 巡检回合之间重置）。
        /// </summary>
        public void ClearMissingKeys()
        {
            _missingKeys?.Clear();
            _missingKeyEvents = 0;
            _hasLoggedMissingCap = false;
        }

        /// <summary>
        /// 当语言改变时调用。
        /// </summary>
        /// <remarks>在全部本地化器重注入<em>之后</em>触发，回调内查询文本即已是新语言。
        /// 需要「关服自动摘除」的订阅请用 <see cref="SubscribeLanguageChanged"/>。</remarks>
        public event Action<Language> OnLanguageChanged;

        /// <summary>存储与解析引擎。延迟创建：处理器会被 Settings 以 SerializeReference 还原，构造器不一定跑。</summary>
        private LocalizationStore Store => _store ??= new LocalizationStore();

        protected override void OnShutdown()
        {
            _currentLanguage = null;
            _settingSource = string.Empty;
            _dataLoaded = false;
            _pendingLanguage = null;
            _hasLoggedLoadError = false;
            _hasLoggedNoLanguage = false;
            _hasLoggedFormatError = false;
            _currentLanguageIndex = -1;
            _isSwitching = false;
            _fallbackChain = Array.Empty<Language>();
            _fallbackIndices = Array.Empty<int>();
            _warnedUnavailableLanguages = null;
            _missingKeys = null;
            _missingKeyEvents = 0;
            _hasLoggedMissingCap = false;
            _formatCulture = null;
            _localizers.Clear();

            if (_subscriptions.Count > 0)
            {
                var snapshot = _subscriptions.ToArray();
                _subscriptions.Clear();
                foreach (var subscription in snapshot)
                {
                    subscription?.Invalidate();
                }
            }

            // 存储连同覆盖层一起丢弃——覆盖层不跨关服存活，否则热改文案会串进下一次会话
            _store?.Clear();
            _store = null;
        }

        #region 数据源 [DATA SOURCE]

        /// <summary>
        /// 加载一批本地化词条（<b>推荐扩展点</b>）。
        /// <para>批自带语言头，列下标顺序即 <see cref="LocalizationTextBatch.Languages"/> 的顺序，
        /// 因此不再依赖「语言取自全局注册表、列序取自反射字段声明序」这类跨文件隐含约定。</para>
        /// </summary>
        internal virtual LocalizationTextBatch LoadLocalizedTextBatch()
        {
            var (languages, strings) = LoadLocalizedData();
            return new LocalizationTextBatch(languages, strings, "legacy");
        }

        /// <summary>
        /// 加载本地化数据源（兼容扩展点）。
        /// <para>改用 <see cref="LoadLocalizedTextBatch"/> 的实现无需再管本方法；保留是为了不打断存量处理器。</para>
        /// </summary>
        /// <returns>语言列表与本地化字符串字典。</returns>
        protected virtual (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData()
            => (new List<Language>(), new Dictionary<string, List<string>>());

        /// <summary>
        /// 懒式加载本地化数据源并解析当前语言。
        /// <para>数据加载依赖资源服务（配置表），服务注册期资源尚未就绪；
        /// 首次访问多语言 API 时资源必然已加载完成，故推迟到调用点执行。</para>
        /// <para>批为空或列数失配都视为数据未就绪，不置成功标记，下次访问自动重试。</para>
        /// </summary>
        private void EnsureLocalizedStringsLoaded()
        {
            if (_dataLoaded) return;

            LoadLocalizedStrings();
            if (Store.LanguageCount == 0) return;

            _dataLoaded = true;
            ResolveFallbackChain();
            // 未就绪期间记录的切换意图优先于首启检测链（该意图来自显式 ChangeLanguage，按用户切换语义持久化）；
            // 无意图时首次自动解析不持久化设置，避免把系统语言固化进存档
            var pending = _pendingLanguage;
            _pendingLanguage = null;
            ChangeLanguage(pending ?? ResolveInitialLanguage(), true, pending != null);
        }

        /// <summary>
        /// 从数据源取一批词条并换入存储。
        /// </summary>
        private void LoadLocalizedStrings()
        {
            LocalizationTextBatch batch;
            Exception sourceError = null;
            try
            {
                batch = LoadLocalizedTextBatch();
            }
            catch (Exception ex)
            {
                // 异常留住不外抛（否则每次查询都抛一遍），交给下面的统一出口只报一次
                batch = null;
                sourceError = ex;
            }

            if (batch == null || batch.Languages.Length == 0)
            {
                // 数据未就绪时每次查询都会重试进入此处，错误日志只打一次
                if (_hasLoggedLoadError) return;
                _hasLoggedLoadError = true;

                // 取数抛了异常时不得念"先生成配置"：那会把一次读表失败说成配表缺失，
                // 真因反倒无人看。异常连堆栈一并落进这条日志
                if (sourceError != null)
                {
                    LogUtility.Error("Failed to load localized text from {0}: {1}", GetType().Name, sourceError);
                    return;
                }

                LogUtility.Error("Failed to load localized text, generate config first!");
                return;
            }

            if (Store.TryApply(batch, out var rejectedKey))
            {
                // 成功即复位闸门：之后再次失败（热更包损坏等）仍应能上报，而不是永久哑火
                _hasLoggedLoadError = false;
                _hasLoggedNoLanguage = false;
                LogUtility.Info("Load Localized Text Success! [{0}] {1} entries x {2} languages",
                    batch.SourceId, batch.Strings.Count, batch.Languages.Length);
                return;
            }

            // 语言列数与语言数失配会表现为"显示错误语言"而非报错，必须在加载期整批拦下；
            // 拒载时保留上一份可用快照——换批失败不该把本来能显示的文案一起抹掉
            if (!_hasLoggedLoadError)
            {
                _hasLoggedLoadError = true;
                LogUtility.Error("Localized strings '{0}' from source '{1}' has a column count that mismatches the {2} declared languages; the whole batch is rejected.",
                    rejectedKey, batch.SourceId, batch.Languages.Length);
            }
        }

        /// <summary>
        /// 强制重载本地化词条（配置表热更、远程词库下发后调用）。
        /// <para>不走 <see cref="EnsureLocalizedStringsLoaded"/>：它能区分「从未加载」与「已加载」，
        /// 但分不出「旧快照还在」与「重载成功」——引用比较换入前后的批快照，
        /// 失败的热更才不会触发一次假的语言变更广播。</para>
        /// <para>未换入新快照时一切保持不动（旧快照、当前语言、已显示文案）；
        /// 换入后当前语言仍在批内则强制重注入并广播（语言未变但词条可能已更新），
        /// 不在批内则按检测/回退/表首项兜底重选。覆盖层按契约不被换批清空。</para>
        /// </summary>
        public void ReloadTexts()
        {
            if (_isSwitching)
            {
                // 与嵌套 ChangeLanguage 同理：注入回调里重载会打乱快照与事件顺序
                LogUtility.Error("ReloadTexts is ignored: a language switch is already in progress.");
                return;
            }

            var previousBatch = Store.Batch;
            LoadLocalizedStrings();
            if (Store.LanguageCount == 0)
            {
                // 从未成功加载过：维持未就绪，后续查询继续走懒加载重试
                _dataLoaded = false;
                return;
            }

            _dataLoaded = true;
            if (ReferenceEquals(Store.Batch, previousBatch)) return;

            ResolveFallbackChain();

            var currentIndex = Store.IndexOf(_currentLanguage);
            if (currentIndex < 0)
            {
                // 当前语言不在新批内（热更砍掉了语言）：重选走完整 ChangeLanguage 流程
                ChangeLanguage(ResolveInitialLanguage(), true, false);
                return;
            }

            _currentLanguageIndex = currentIndex;
            ReinjectLocalizers();
            RaiseLanguageChanged(_currentLanguage);
        }

        /// <summary>
        /// 解析回退链配置：把语言 Code 换成批内真实存在的语言与其列下标。
        /// <para>识别不了的语言 Code 会被剔除并警告——静默落到默认语言会让配置错误一路带到上线。</para>
        /// </summary>
        private void ResolveFallbackChain()
        {
            _fallbackChain = Array.Empty<Language>();
            _fallbackIndices = Array.Empty<int>();

            if (m_FallbackLanguageCodes == null || m_FallbackLanguageCodes.Length == 0) return;

            var chain = new List<Language>(m_FallbackLanguageCodes.Length);
            var indices = new List<int>(m_FallbackLanguageCodes.Length);
            foreach (var code in m_FallbackLanguageCodes)
            {
                if (string.IsNullOrEmpty(code)) continue;

                if (!LocalizationService.TryGetBuiltInLanguage(code, out var language))
                {
                    LogUtility.Warning("Fallback language '{0}' is not a built-in language Name/Code, skipped.", code);
                    continue;
                }

                var index = Store.IndexOf(language);
                if (index == -1)
                {
                    LogUtility.Warning("Fallback language {0} is not present in the localized data, skipped.", language);
                    continue;
                }

                if (chain.Contains(language)) continue;

                chain.Add(language);
                indices.Add(index);
            }

            _fallbackChain = chain.ToArray();
            _fallbackIndices = indices.ToArray();
        }

        /// <summary>
        /// 解析首启语言：检测链结果优先，不在批内时按回退链、再按语言表首项兜底。
        /// <para>检测链给出的语言完全可能没随包发行（中文系统跑只出英日两语的包）。
        /// 早退会让 <see cref="_currentLanguage"/> 停在 null，于是<b>每一条</b>查询都露出 ID——
        /// 首启必须落在一个真实存在的语言上。</para>
        /// </summary>
        private Language ResolveInitialLanguage()
        {
            var detectedIndex = Store.IndexOf(CurrentLanguage);
            if (detectedIndex >= 0) return Store.LanguageAt(detectedIndex);

            var fallbackIndices = _fallbackIndices;
            if (fallbackIndices.Length > 0) return Store.LanguageAt(fallbackIndices[0]);

            return Store.LanguageAt(0);
        }

        #endregion

        #region 语言切换 [LANGUAGE SWITCH]

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        public void ChangeLanguage(Language language, bool logSource = false) => ChangeLanguage(language, logSource, true);

        /// <summary>
        /// 更改当前语言的内部实现。
        /// </summary>
        /// <param name="persist">是否持久化设置；仅用户显式切换为 true，首次自动解析不写入，避免固化系统语言。</param>
        private void ChangeLanguage(Language language, bool logSource, bool persist)
        {
            if (_isSwitching)
            {
                // 注入回调内嵌套切换会让快照与事件顺序失效，且极易造成无限递归
                LogUtility.Error("ChangeLanguage({0}) is ignored: another language switch is already in progress.", language);
                return;
            }

            EnsureLocalizedStringsLoaded();

            if (Store.LanguageCount == 0)
            {
                // 数据未就绪（配置表未生成/包未下载完）：记录切换意图，首次加载成功时优先应用，
                // 用户在启动早期改语言不该被静默吞掉后落回检测链默认
                _pendingLanguage = language;
                LogNoLanguageOnce();
                return;
            }

            if (_currentLanguage == language) return;

            var languageIndex = Store.IndexOf(language);
            if (languageIndex == -1)
            {
                WarnLanguageUnavailable(language);
                return;
            }

            _isSwitching = true;
            try
            {
                _currentLanguage = Store.LanguageAt(languageIndex);
                _currentLanguageIndex = languageIndex;
                _formatCulture = ResolveFormatCulture(_currentLanguage);

                ReinjectLocalizers();
                RaiseLanguageChanged(_currentLanguage);

                if (persist) SettingUtility.SetString(GameConstant.Setting.LANGUAGE, _currentLanguage.Code);
                LogUtility.Info($"Change the language: {_currentLanguage}{(logSource ? $"(by {_settingSource})" : "")}");
            }
            finally
            {
                _isSwitching = false;
            }
        }

        /// <summary>
        /// 重注入全部本地化器。先重注入再抛事件：订阅者在 OnLanguageChanged 回调里取文本必须已拿到新语言。
        /// 快照遍历 + 异常隔离，单个本地化器失败不影响其余，也防注入期间销毁导致的集合变更。
        /// </summary>
        private void ReinjectLocalizers()
        {
            var snapshot = _localizers.ToArray();
            foreach (var localizer in snapshot)
            {
                if (localizer == null) continue;

                try
                {
                    localizer.Localize();
                }
                catch (Exception ex)
                {
                    LogUtility.Error(ex);
                }
            }
        }

        private void LogNoLanguageOnce()
        {
            if (_hasLoggedNoLanguage) return;

            _hasLoggedNoLanguage = true;
            LogUtility.Error("No language available!");
        }

        private void RaiseLanguageChanged(Language language)
        {
            OnLanguageChanged?.Invoke(language);

            if (_subscriptions.Count == 0) return;

            // 与本地化器同等待遇：快照遍历 + 单个订阅者抛异常不牵连同一次派发的其余订阅者
            var snapshot = _subscriptions.ToArray();
            foreach (var subscription in snapshot)
            {
                var callback = subscription?.Callback;
                if (callback == null) continue;

                try
                {
                    callback(language);
                }
                catch (Exception ex)
                {
                    LogUtility.Error(ex);
                }
            }
        }

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">要切换的语言Name或Code</param>
        /// <remarks>不区分大小写。例如简体中文 => "ChineseSimplified" "zh-Hans" "chineseSimplified"均可。
        /// 只做身份解析（无法识别的输入回落默认语言）；语言是否随包发行由批内可用性校验判定并告警，
        /// 不再依赖任何全局注册表，未加载时也不会被静默回落默认语言。</remarks>
        public void ChangeLanguage(string language) => ChangeLanguage(LocalizationService.ToLanguage(language, false));

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="index">要切换已加载的语言索引</param>
        public void ChangeLanguage(int index)
        {
            EnsureLocalizedStringsLoaded();
            var count = Store.LanguageCount;
            if (index < 0 || index >= count)
            {
                LogUtility.Error("Language index {0} out of range [0, {1}).", index, count);
                return;
            }

            ChangeLanguage(Store.LanguageAt(index));
        }

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public string ActivatePreviousLanguage()
        {
            EnsureLocalizedStringsLoaded();
            var count = Store.LanguageCount;
            if (count == 0)
            {
                LogNoLanguageOnce();
                return null;
            }

            var prevIndex = (int)Mathf.Repeat(CurrentLanguageIndex - 1, count);
            ChangeLanguage(Store.LanguageAt(prevIndex));
            return Store.LanguageAt(prevIndex).Name;
        }

        /// <summary>
        /// 激活下一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public string ActivateNextLanguage()
        {
            EnsureLocalizedStringsLoaded();
            var count = Store.LanguageCount;
            if (count == 0)
            {
                LogNoLanguageOnce();
                return null;
            }

            var nextIndex = (int)Mathf.Repeat(CurrentLanguageIndex + 1, count);
            ChangeLanguage(Store.LanguageAt(nextIndex));
            return Store.LanguageAt(nextIndex).Name;
        }

        private void WarnLanguageUnavailable(Language language)
        {
            _warnedUnavailableLanguages ??= new HashSet<Language>();
            if (!_warnedUnavailableLanguages.Add(language)) return;

            LogUtility.Warning("Language {0} is not available.", language);
        }

        /// <summary>
        /// 当前语言的格式化文化（数字/日期等随游戏语言而非设备系统文化）。
        /// </summary>
        /// <remarks>SerializeReference 还原的处理器不保证跑过构造器，读取侧做不变文化兜底。</remarks>
        internal CultureInfo FormatCulture => _formatCulture ?? CultureInfo.InvariantCulture;

        /// <summary>
        /// 按语言 Code 解析格式化文化；未知/自定义语言 Code 回落 <see cref="CultureInfo.InvariantCulture"/>。
        /// </summary>
        private static CultureInfo ResolveFormatCulture(Language language)
        {
            if (language == null) return CultureInfo.InvariantCulture;

            try
            {
                return CultureInfo.GetCultureInfo(language.Code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        #endregion

        #region 订阅与本地化器 [SUBSCRIPTIONS & LOCALIZERS]

        /// <summary>
        /// 以句柄订阅语言变更。
        /// <para>与 <see cref="OnLanguageChanged"/> 在同一次派发里触发、时序契约一致（重注入之后）；
        /// 区别是句柄 <c>Dispose</c> 即摘除，且<b>关服时框架统一作废</b>。</para>
        /// </summary>
        public IDisposable SubscribeLanguageChanged(Action<Language> callback)
        {
            if (callback == null) return LanguageChangeSubscription.Completed;

            var subscription = new LanguageChangeSubscription(this, callback);
            _subscriptions.Add(subscription);
            return subscription;
        }

        internal void Unsubscribe(LanguageChangeSubscription subscription)
        {
            var index = _subscriptions.IndexOf(subscription);
            if (index >= 0) _subscriptions.RemoveAt(index);
        }

        /// <summary>
        /// 添加本地化器（幂等去重：重复注册同一实例不得产生第二次重注入）。
        /// </summary>
        public void AddLocalizer(LocalizerBase localizer)
        {
            if (localizer == null || _localizers.Contains(localizer)) return;

            _localizers.Add(localizer);
        }

        /// <summary>
        /// 移除本地化器
        /// </summary>
        public void RemoveLocalizer(LocalizerBase localizer) => _localizers.Remove(localizer);

        #endregion

        #region 文本查询 [TEXT QUERIES]

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID。
        /// </summary>
        /// <remarks>只断言词条存在，不代表当前语言已有译文（缺译时仍会命中覆盖层、回退链或返回 ID）。</remarks>
        public bool Has(string id)
        {
            EnsureLocalizedStringsLoaded();
            return Store.HasKey(id);
        }

        /// <summary>
        /// 根据文本 ID 获取当前语言的本地化字符串。
        /// </summary>
        /// <remarks>格式化文化跟随当前游戏语言（<see cref="FormatCulture"/>），不随设备系统文化漂移——
        /// 德语设备跑英语包时数字仍显示为「1.5」而非「1,5」。</remarks>
        /// <param name="id">文本 ID</param>
        /// <param name="p">Format</param>
        public string GetTextFromId(string id, params object[] p)
        {
            EnsureLocalizedStringsLoaded();

            var text = ResolveRaw(id, _currentLanguage);
            if (text == null) return id;
            if (p is not { Length: > 0 }) return text;

            try
            {
                return string.Format(FormatCulture, text, p);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, p.Length);
                return text;
            }
        }

        /// <summary>
        /// 根据文本 ID 获取带一个格式化参数的本地化字符串。
        /// </summary>
        /// <remarks>走 <see cref="StringUtility.Format{T1}(string,T1)"/>：装了 ZString 时不装箱、不建参数数组；
        /// 未装 ZString 时退化到 <c>StringBuilder.AppendFormat</c>，那条路径仍会装箱。
        /// 文化边界：ZString 快路径下基元数字按不变规则格式化（不随文化漂移），自定义 <see cref="IFormattable"/> 实参按其默认文化；
        /// 需要严格跟随游戏语言文化（日期/货币/小数分隔符）时改用 <see cref="GetTextFromId(string,object[])"/>。
        /// 参数超过 4 个的文案请改用 <see cref="GetTextFromId(string,object[])"/>，并考虑把它拆成两条 ID。</remarks>
        public string GetTextFromId<T1>(string id, T1 arg1)
        {
            EnsureLocalizedStringsLoaded();

            var text = ResolveRaw(id, _currentLanguage);
            if (text == null) return id;

            try
            {
                return StringUtility.Format(text, arg1);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, 1);
                return text;
            }
        }

        /// <summary>根据文本 ID 获取带两个格式化参数的本地化字符串（装箱边界说明见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public string GetTextFromId<T1, T2>(string id, T1 arg1, T2 arg2)
        {
            EnsureLocalizedStringsLoaded();

            var text = ResolveRaw(id, _currentLanguage);
            if (text == null) return id;

            try
            {
                return StringUtility.Format(text, arg1, arg2);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, 2);
                return text;
            }
        }

        /// <summary>根据文本 ID 获取带三个格式化参数的本地化字符串（装箱边界说明见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public string GetTextFromId<T1, T2, T3>(string id, T1 arg1, T2 arg2, T3 arg3)
        {
            EnsureLocalizedStringsLoaded();

            var text = ResolveRaw(id, _currentLanguage);
            if (text == null) return id;

            try
            {
                return StringUtility.Format(text, arg1, arg2, arg3);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, 3);
                return text;
            }
        }

        /// <summary>根据文本 ID 获取带四个格式化参数的本地化字符串（装箱边界说明见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public string GetTextFromId<T1, T2, T3, T4>(string id, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            EnsureLocalizedStringsLoaded();

            var text = ResolveRaw(id, _currentLanguage);
            if (text == null) return id;

            try
            {
                return StringUtility.Format(text, arg1, arg2, arg3, arg4);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, 4);
                return text;
            }
        }

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串。
        /// </summary>
        /// <remarks>格式化文化跟随<em>被查询的语言</em>（而非当前语言），与译文语义一致。</remarks>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言；<c>null</c> 表示当前语言</param>
        /// <param name="p">Format</param>
        public string GetTextFromIdLanguage(string id, Language language, params object[] p)
        {
            EnsureLocalizedStringsLoaded();

            var effectiveLanguage = language ?? _currentLanguage;
            var text = ResolveRaw(id, effectiveLanguage);
            if (text == null) return id;
            if (p is not { Length: > 0 }) return text;

            var culture = effectiveLanguage == _currentLanguage ? FormatCulture : ResolveFormatCulture(effectiveLanguage);
            try
            {
                return string.Format(culture, text, p);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, p.Length);
                return text;
            }
        }

        /// <summary>
        /// 按「覆盖层 → 指定语言 → 回退链」取原始译文；全链缺译时返回 <c>null</c>。调用方须已确保数据加载完成。
        /// </summary>
        private string ResolveRaw(string id, Language language)
        {
            // ID 为空、或词条整个不存在时无列可回退，一律由调用方露出 ID
            if (string.IsNullOrEmpty(id)) return null;

            // 当前语言的列下标已在切换时缓存——查询热路径不再每次线性扫语言表（回退链同样是预解析下标）
            var index = language == _currentLanguage ? _currentLanguageIndex : Store.IndexOf(language);
            var text = Store.Resolve(id, language, index, _fallbackChain, _fallbackIndices);
            // 数据未加载时整库为空，「查不到」不等于「缺译」，不记录
            if (text == null && _dataLoaded) TrackMissingKey(id, language);
            return text;
        }

        /// <summary>
        /// 记录一次全链缺译：去重进集合、逐 key 告警一次；超容量后仅保留计数。
        /// </summary>
        private void TrackMissingKey(string id, Language language)
        {
            _missingKeyEvents++;
            _missingKeys ??= new HashSet<string>();

            if (!_missingKeys.Contains(id) && _missingKeys.Count >= MAX_TRACKED_MISSING_KEYS)
            {
                if (!_hasLoggedMissingCap)
                {
                    _hasLoggedMissingCap = true;
                    LogUtility.Warning("Missing localization keys exceeded {0}; further misses are counted but no longer tracked individually.", MAX_TRACKED_MISSING_KEYS);
                }

                return;
            }

            if (_missingKeys.Add(id))
            {
                LogUtility.Warning("Localized text '{0}' is missing for language '{1}' (fallback chain exhausted); the key itself is displayed.",
                    id, language != null ? language.Code : "<unresolved>");
            }
        }

        private void LogFormatError(string id, string text, int argCount)
        {
            // 一条文案写坏占位符不该把整块界面的查询抛出去，日志同样只打一次
            if (_hasLoggedFormatError) return;

            _hasLoggedFormatError = true;
            LogUtility.Error("Localized text '{0}' has invalid placeholders for {1} argument(s): \"{2}\"", id, argCount, text);
        }

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典（键为语言 Name；同名语言重复时后者覆盖前者，不再抛异常）。
        /// </summary>
        public Dictionary<string, string> GetDictionaryFromId(string id)
        {
            EnsureLocalizedStringsLoaded();

            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(id) || !Store.HasKey(id)) return dict;

            for (var i = 0; i < Store.LanguageCount; i++)
            {
                var language = Store.LanguageAt(i);
                var text = ResolveRaw(id, language) ?? id;
                dict[language.Name] = text;
            }

            return dict;
        }

        /// <summary>
        /// 获取所有多语言索引
        /// </summary>
        public List<string> GetAllIds()
        {
            EnsureLocalizedStringsLoaded();
            return Store.Batch.Strings.Keys.ToList();
        }

        #endregion

        #region 运行时覆盖 [RUNTIME OVERLAY]

        /// <summary>
        /// 覆盖指定语言下的一批词条（运营热改文案、QA 强改、远程补丁走同一条路）。
        /// <para>叠加语义：未覆盖的词条仍取批内译文，值为空/仅空白等同于「不覆盖」；
        /// 覆盖层不会被换批动作清空，也不会跨关服存活。同名来源即同一层，按 key 合并。</para>
        /// </summary>
        /// <param name="sourceId">来源标识（诊断用，如 remote-ops / qa-force）。</param>
        /// <param name="language">被覆盖的语言；不在当前批内时忽略。</param>
        /// <param name="entries">key → 新译文。</param>
        /// <returns>该来源层累计的覆盖条数；参数不合法或语言未收录时为 -1。</returns>
        public int SetStringOverlay(string sourceId, Language language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            EnsureLocalizedStringsLoaded();

            if (string.IsNullOrEmpty(sourceId) || entries == null || Store.IndexOf(language) == -1) return -1;

            return Store.SetOverlay(sourceId, language, entries);
        }

        /// <summary>撤掉某个来源的全部覆盖；返回是否确实存在该层。</summary>
        public bool ClearStringOverlay(string sourceId) => !string.IsNullOrEmpty(sourceId) && Store.ClearOverlay(sourceId);

        /// <summary>撤掉全部覆盖层。</summary>
        public void ClearAllStringOverlays() => Store.ClearAllOverlays();

        /// <summary>已登记的覆盖层数量。</summary>
        public int StringOverlayLayerCount
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return Store.OverlayLayerCount;
            }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="LocalizationServiceHandler.SubscribeLanguageChanged"/> 的订阅句柄。
    /// <para>Dispose 幂等；处理器关服时由框架统一作废，此后 Dispose 只是空操作。</para>
    /// </summary>
    public sealed class LanguageChangeSubscription : IDisposable
    {
        internal static readonly LanguageChangeSubscription Completed = new LanguageChangeSubscription();

        private LocalizationServiceHandler _handler;
        private Action<Language> _callback;

        internal Action<Language> Callback => _callback;

        private LanguageChangeSubscription()
        {
        }

        internal LanguageChangeSubscription(LocalizationServiceHandler handler, Action<Language> callback)
        {
            _handler = handler;
            _callback = callback;
        }

        /// <summary>是否仍在订阅表中（未 Dispose 且处理器未关服）。</summary>
        public bool IsSubscribed => _handler != null;

        internal void Invalidate()
        {
            _handler = null;
            _callback = null;
        }

        public void Dispose()
        {
            var handler = _handler;
            if (handler == null) return;

            Invalidate();
            handler.Unsubscribe(this);
        }
    }
}
