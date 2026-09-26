using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>承载语言管理、语言切换、运行时覆盖与文本查询；词条存储与取值解析落在
    /// <see cref="LocalizationStore"/>，与编辑器预览共用同一套解析路径。</para>
    /// <para>默认整批加载、全语言常驻；数据源自报支持按语言取列时转为列模式，常驻与取值都只有当前语言列。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationServiceHandler : FrameworkHandler
    {
        // 本地化器列表
        [NonSerialized] internal readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();
        // 注册去重集合：与 _localizers 同进退——万级本地化器场景加载期注册 List.Contains 是 O(N²)，
        // HashSet 把判定收成 O(1)（列表保留注册序，重注入按序执行）
        [NonSerialized] private HashSet<LocalizerBase> _localizerSet;
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
        // 不存在的语言只在切换时警告一次
        [NonSerialized] private HashSet<Language> _warnedUnavailableLanguages;
        // 缺译追踪：覆盖层与当前语言都给不出译文的 key，去重记录，供 QA 巡检与调试面板展示
        [NonSerialized] private HashSet<string> _missingKeys;
        // 缺译事件总数（含同一 key 的重复命中）——与去重集合对照可分辨「大面积漏翻」与「高频单点漏翻」
        [NonSerialized] private int _missingKeyEvents;
        // 追踪集合饱和告警只打一次（饱和后计数照走，逐 key 记录与告警停摆，防异常配置刷爆内存与日志）
        [NonSerialized] private bool _hasLoggedMissingCap;
        // 格式化文化：跟随当前游戏语言（数字/日期分隔符与文案语言一致），语言切换时重解析；解析失败回落不变文化
        [NonSerialized] private CultureInfo _formatCulture;
        // 异步预加载在途标记：在途期间同步懒加载按未就绪降级（查询返回 ID 原文），并发 LoadAsync 共享同一任务
        [NonSerialized] private bool _isAsyncLoading;
        [NonSerialized] private UniTask _pendingLoadAsync;

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

        /// <summary>当前语言是否从右向左书写（未就绪为 <c>false</c>）。</summary>
        internal bool IsCurrentLanguageRightToLeft => _currentLanguage?.IsRightToLeft ?? false;

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
            _warnedUnavailableLanguages = null;
            _missingKeys = null;
            _missingKeyEvents = 0;
            _hasLoggedMissingCap = false;
            _formatCulture = null;
            _isAsyncLoading = false;
            _pendingLoadAsync = default;
            _localizers.Clear();
            _localizerSet?.Clear();
            _localizerSet = null;

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
        /// 异步加载本地化词条（<b>异步扩展点</b>）。
        /// <para>默认实现直接包装同步批加载（<see cref="LoadLocalizedTextBatch"/>），
        /// 大数据源（远程词库、分通道流式表）应覆写为真正的异步实现。</para>
        /// </summary>
        internal virtual UniTask<LocalizationTextBatch> LoadLocalizedTextBatchAsync()
            => UniTask.FromResult(LoadLocalizedTextBatch());

        /// <summary>
        /// 异步确保本地化数据已加载（启动期预热的推荐入口）。
        /// <para>幂等 + 在途去重：并发调用共享同一任务；已加载时立即完成。
        /// 完成后与同步首载走同一条语言解析 + 重注入 + 广播路径。</para>
        /// </summary>
        public UniTask LoadAsync()
        {
            if (_dataLoaded) return UniTask.CompletedTask;
            if (_isAsyncLoading) return _pendingLoadAsync;

            _isAsyncLoading = true;
            // Preserve 语义：并发调用方共享并各自等待同一任务——UniTask 默认禁止二次等待
            _pendingLoadAsync = LoadAsyncCore().Preserve();
            return _pendingLoadAsync;
        }

        /// <summary>异步加载期间是否仍在途（诊断用，不触发任何加载）。</summary>
        internal bool IsLoading => _isAsyncLoading;

        /// <summary>
        /// 异步加载核心：取批、换入、语言解析与首启切换。
        /// </summary>
        private async UniTask LoadAsyncCore()
        {
            try
            {
                if (SupportsPerLanguageLoad)
                {
                    // 列模式无"整批"可取：让出一帧后装语言头，列按需装载
                    await UniTask.Yield();
                    EnsurePerLanguageHeaderLoaded();
                }
                else
                {
                    LocalizationTextBatch batch;
                    Exception sourceError = null;
                    try
                    {
                        batch = await LoadLocalizedTextBatchAsync();
                    }
                    catch (Exception ex)
                    {
                        // 与同步路径同一约定：异常不外抛，交统一出口只报一次
                        batch = null;
                        sourceError = ex;
                    }

                    // 加载在途时处理器被关服：结果一律丢弃，不得把批写进下一次会话的新存储
                    if (!IsInitialized) return;

                    ApplyLoadedBatch(batch, sourceError);
                }
            }
            finally
            {
                _isAsyncLoading = false;
                _pendingLoadAsync = default;
            }

            if (Store.LanguageCount == 0) return;

            CompleteLoad();
        }

        /// <summary>
        /// 声明本处理器支持按语言列加载（<b>可选契约</b>）。
        /// <para>默认 <c>false</c>：整批加载、全语言常驻。数据源可按语言单独取列时覆写为 <c>true</c>
        /// 并实现 <see cref="LoadLanguageHeader"/> 与 <see cref="LoadLanguageColumn"/>，
        /// 常驻即降为「语言头 + 当前语言列」，是否值得启用按 <see cref="ResidentChars"/> 量级判断。</para>
        /// </summary>
        protected virtual bool SupportsPerLanguageLoad => false;

        /// <summary>
        /// 取语言头（可用语言与其列序；<b>按语言列模式必须实现</b>）。
        /// </summary>
        /// <returns>可用语言列表；<c>null</c>/空表示未就绪（保持重试，与整批空批同语义）。</returns>
        protected virtual IReadOnlyList<Language> LoadLanguageHeader() => null;

        /// <summary>
        /// 取单一语言列：<b>按语言列模式必须实现</b>。
        /// </summary>
        /// <param name="language">目标语言（来自语言头）。</param>
        /// <returns>key → 译文；返回 <c>null</c> 视为加载失败（下次访问重试），
        /// 空字典视为「已加载的空列」（语言在头内但暂无词条，不再重试）。</returns>
        protected virtual Dictionary<string, string> LoadLanguageColumn(Language language) => null;

        /// <summary>
        /// 懒式加载本地化数据源并解析当前语言。
        /// <para>数据加载依赖资源服务（配置表），服务注册期资源尚未就绪；
        /// 首次访问多语言 API 时资源必然已加载完成，故推迟到调用点执行。</para>
        /// <para>批为空或列数失配都视为数据未就绪，不置成功标记，下次访问自动重试；
        /// 异步预加载在途期间不抢跑——查询按未就绪降级（返回 ID 原文），避免同源两路并发加载。</para>
        /// </summary>
        private void EnsureLocalizedStringsLoaded()
        {
            if (_dataLoaded || _isAsyncLoading) return;

            if (SupportsPerLanguageLoad)
            {
                EnsurePerLanguageHeaderLoaded();
            }
            else
            {
                LoadLocalizedStrings();
            }

            if (Store.LanguageCount == 0) return;

            CompleteLoad();
        }

        /// <summary>
        /// 数据就绪后的统一收口：置已载标记、落实语言切换意图/首启检测。
        /// <para>同步懒加载、异步预加载、按语言列模式共用——保证三条路径的语言解析与持久化语义完全一致。</para>
        /// </summary>
        private void CompleteLoad()
        {
            _dataLoaded = true;
            // 未就绪期间记录的切换意图优先于首启检测链（该意图来自显式 ChangeLanguage，按用户切换语义持久化）；
            // 无意图时首次自动解析不持久化设置，避免把系统语言固化进存档
            var pending = _pendingLanguage;
            _pendingLanguage = null;
            ChangeLanguage(pending ?? ResolveInitialLanguage(), true, pending != null);
        }

        /// <summary>
        /// 装载按语言列模式的语言头（幂等；头为空保持重试，错误只报一次）。
        /// </summary>
        private void EnsurePerLanguageHeaderLoaded()
        {
            if (Store.LanguageCount > 0) return;

            IReadOnlyList<Language> header = null;
            Exception error = null;
            try
            {
                header = LoadLanguageHeader();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (header == null || header.Count == 0)
            {
                if (!_hasLoggedLoadError)
                {
                    _hasLoggedLoadError = true;
                    if (error != null)
                    {
                        LogUtility.Error("Failed to load localization language header from {0}: {1}", GetType().Name, error);
                    }
                    else
                    {
                        LogUtility.Error("Failed to load localization language header, generate config first!");
                    }
                }

                return;
            }

            _hasLoggedLoadError = false;
            _hasLoggedNoLanguage = false;

            // 头去重（重复语言只留首个——列下标按首现序）
            var languages = new List<Language>(header.Count);
            for (var i = 0; i < header.Count; i++)
            {
                if (header[i] != null && !languages.Contains(header[i])) languages.Add(header[i]);
            }

            Store.BeginSparse(languages.ToArray());
            LogUtility.Info("Localization language header loaded ({0} languages, per-language mode).", languages.Count);
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

            ApplyLoadedBatch(batch, sourceError);
        }

        /// <summary>
        /// 把取到的批（或取数异常）换入存储。
        /// <para>同步懒加载与异步预加载共用的统一出口：异常只报一次、成功复位闸门、列数失调整批拒载并保留旧快照。</para>
        /// </summary>
        private void ApplyLoadedBatch(LocalizationTextBatch batch, Exception sourceError)
        {
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
        /// 但分不出「旧快照还在」与「重载成功」——按换批世代号比较换入前后的快照，
        /// 失败的热更才不会触发一次假的语言变更广播。</para>
        /// <para>未换入新快照时一切保持不动（旧快照、当前语言、已显示文案）；
        /// 换入后当前语言仍在批内则强制重注入并广播（语言未变但词条可能已更新），
        /// 不在批内则按检测链、再按语言表首项兜底重选。覆盖层按契约不被换批清空。</para>
        /// </summary>
        public void ReloadTexts()
        {
            if (_isSwitching)
            {
                // 与嵌套 ChangeLanguage 同理：注入回调里重载会打乱快照与事件顺序
                LogUtility.Error("ReloadTexts is ignored: a language switch is already in progress.");
                return;
            }

            if (SupportsPerLanguageLoad)
            {
                ReloadPerLanguageTexts();
                return;
            }

            var previousGeneration = Store.Generation;
            LoadLocalizedStrings();
            if (Store.LanguageCount == 0)
            {
                // 从未成功加载过：维持未就绪，后续查询继续走懒加载重试
                _dataLoaded = false;
                return;
            }

            _dataLoaded = true;
            if (Store.Generation == previousGeneration) return;

            ReapplyCurrentLanguageAfterReload();
        }

        /// <summary>
        /// 换批后落实当前语言：仍在批内则强制重注入并广播（语言未变词条可能已更新），
        /// 不在批内（热更砍掉了语言）则按检测链、再按语言表首项兜底重选。
        /// </summary>
        private void ReapplyCurrentLanguageAfterReload()
        {
            var currentIndex = Store.IndexOf(_currentLanguage);
            if (currentIndex < 0)
            {
                ChangeLanguage(ResolveInitialLanguage(), true, false);
                return;
            }

            _currentLanguageIndex = currentIndex;
            ReinjectLocalizers();
            RaiseLanguageChanged(_currentLanguage);
        }

        /// <summary>
        /// 按语言列模式的热重载：重取语言头与列缓存。
        /// <para>头取不到时保留旧快照（与整批拒载同语义）；头变更后当前语言不在新头内则按兜底重选。
        /// 覆盖层按契约不被重载清空。</para>
        /// </summary>
        private void ReloadPerLanguageTexts()
        {
            IReadOnlyList<Language> header = null;
            Exception error = null;
            try
            {
                header = LoadLanguageHeader();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (header == null || header.Count == 0)
            {
                // 头坏了不把旧文案一起抹掉；错误只报一次
                if (!_hasLoggedLoadError)
                {
                    _hasLoggedLoadError = true;
                    LogUtility.Error("Failed to reload localization language header{0}; keeping the previous snapshot.", error != null ? ": " + error : " (source returned no data)");
                }

                return;
            }

            Store.ClearData();

            var languages = new List<Language>(header.Count);
            for (var i = 0; i < header.Count; i++)
            {
                if (header[i] != null && !languages.Contains(header[i])) languages.Add(header[i]);
            }

            Store.BeginSparse(languages.ToArray());

            _dataLoaded = true;

            var currentIndex = Store.IndexOf(_currentLanguage);
            if (currentIndex < 0)
            {
                // 当前语言被热更砍掉：走完整切换流程重选（列装载含在其中）
                ChangeLanguage(ResolveInitialLanguage(), true, false);
                return;
            }

            _currentLanguageIndex = currentIndex;
            // 目标列必须先装上再重注入：列装载失败宁可不切广播也不能让界面整屏露 key
            if (!EnsureColumnLoaded(currentIndex, true)) return;

            ReinjectLocalizers();
            RaiseLanguageChanged(_currentLanguage);
        }

        /// <summary>
        /// 解析首启语言：检测链结果优先，没随这批词条发行时按语言表首项兜底。
        /// <para>检测链给出的语言完全可能没随包发行（中文系统跑只出英日两语的包）。
        /// 早退会让 <see cref="_currentLanguage"/> 停在 null，于是<b>每一条</b>查询都露出 ID——
        /// 首启必须落在一个真实存在的语言上。</para>
        /// </summary>
        private Language ResolveInitialLanguage()
        {
            var detectedIndex = Store.IndexOf(CurrentLanguage);
            return detectedIndex >= 0 ? Store.LanguageAt(detectedIndex) : Store.LanguageAt(0);
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

            if (SupportsPerLanguageLoad && !EnsureColumnLoaded(languageIndex, true)) return;

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
        /// 幂等装载一种语言的列。
        /// </summary>
        /// <param name="index">语言头内列下标。</param>
        /// <param name="required"><c>true</c> 表示这次装载是前置条件（失败拒绝切换）；
        /// <c>false</c> 表示补齐（失败跳过，下次再试）。</param>
        private bool EnsureColumnLoaded(int index, bool required)
        {
            if (Store.IsColumnLoaded(index)) return true;

            var language = Store.LanguageAt(index);
            Dictionary<string, string> column = null;
            Exception error = null;
            try
            {
                column = LoadLanguageColumn(language);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (column == null)
            {
                // 加载失败不标记——下次访问自然重试；错误日志只报一次（与整批同源）
                if (!_hasLoggedLoadError)
                {
                    _hasLoggedLoadError = true;
                    if (error != null)
                    {
                        LogUtility.Error("Failed to load localized column for {0}: {1}", language, error);
                    }
                    else
                    {
                        LogUtility.Error("Failed to load localized column for {0} (source returned no data).", language);
                    }
                }

                return !required;
            }

            _hasLoggedLoadError = false;
            Store.ApplyColumn(index, column);
            return true;
        }

        /// <summary>
        /// 重注入全部本地化器。先重注入再抛事件：订阅者在 OnLanguageChanged 回调里取文本必须已拿到新语言。
        /// 快照遍历 + 异常隔离，单个本地化器失败不影响其余，也防注入期间销毁导致的集合变更。
        /// <para>快照从 <see cref="ArrayPool{T}"/> 租用：万级本地化器下每次切换不再落一个引用数组的
        /// 常驻垃圾，租用后清零归还，池也不替已销毁的本地化器续命。</para>
        /// </summary>
        private void ReinjectLocalizers()
        {
            var count = _localizers.Count;
            if (count == 0) return;

            var snapshot = ArrayPool<LocalizerBase>.Shared.Rent(count);
            try
            {
                _localizers.CopyTo(snapshot);
                for (var i = 0; i < count; i++)
                {
                    var localizer = snapshot[i];
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
            finally
            {
                Array.Clear(snapshot, 0, count);
                ArrayPool<LocalizerBase>.Shared.Return(snapshot);
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

            var count = _subscriptions.Count;
            if (count == 0) return;

            // 与本地化器同等待遇：池化快照遍历 + 单个订阅者抛异常不牵连同一次派发的其余订阅者
            var snapshot = ArrayPool<LanguageChangeSubscription>.Shared.Rent(count);
            try
            {
                _subscriptions.CopyTo(snapshot);
                for (var i = 0; i < count; i++)
                {
                    var callback = snapshot[i]?.Callback;
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
            finally
            {
                Array.Clear(snapshot, 0, count);
                ArrayPool<LanguageChangeSubscription>.Shared.Return(snapshot);
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
            if (localizer == null) return;

            _localizerSet ??= new HashSet<LocalizerBase>();
            if (!_localizerSet.Add(localizer)) return;

            _localizers.Add(localizer);
        }

        /// <summary>
        /// 移除本地化器
        /// </summary>
        public void RemoveLocalizer(LocalizerBase localizer)
        {
            if (localizer == null) return;

            _localizerSet?.Remove(localizer);
            _localizers.Remove(localizer);
        }

        #endregion

        #region 文本查询 [TEXT QUERIES]

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID。
        /// </summary>
        /// <remarks>只断言词条存在，不代表当前语言已有译文（该格留空时命中覆盖层或直接返回 ID）。</remarks>
        public bool Has(string id)
        {
            EnsureLocalizedStringsLoaded();
            return Store.HasKey(id);
        }

        /// <summary>
        /// 单趟按 ID 取当前语言译文：命中与否用返回值区分，不把缺译伪装成译文原文。
        /// </summary>
        /// <remarks>与 <see cref="GetTextFromId(string,object[])"/> 同一条解析路径（覆盖层 → 当前语言，
        /// 缺译按既有口径追踪一次），但只查一趟字典——本地化器注入前的「有则注、无则报」判断
        /// 不该比取值本身多花一倍查询；需要原文回显的调用方仍用 <c>GetTextFromId</c> 族。</remarks>
        /// <param name="id">文本 ID。</param>
        /// <param name="text">命中的译文；词条缺失或当前语言留空时为 <c>null</c>。</param>
        /// <returns>取到译文时为 <c>true</c>；缺译或数据未就绪时为 <c>false</c>。</returns>
        public bool TryGetTextFromId(string id, out string text)
        {
            EnsureLocalizedStringsLoaded();

            text = ResolveRaw(id, _currentLanguage);
            return text != null;
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
        /// 取复数词条：按当前语言的 CLDR cardinal 规则在 <c>id#zero|one|two|few|many|other</c> 中选中类别，
        /// 回落顺序 <c>id#类别 → id#other → id</c> 裸 key；全链缺失按缺译处理（追踪 + 告警一次）并返回 ID 原文。
        /// </summary>
        /// <remarks>占位符约定：<c>{0}</c> = 数量，<c>{1..}</c> = 调用方参数——复数文案不该让调用方再手写一遍 count。
        /// 格式化文化跟随当前语言。</remarks>
        /// <param name="id">复数词条基础 ID。</param>
        /// <param name="count">数量（决定 CLDR 类别，同时作为 <c>{0}</c>）。</param>
        /// <param name="p">附加格式化参数（对应 <c>{1}</c> 起的占位符）。</param>
        public string GetPluralTextFromId(string id, long count, params object[] p)
        {
            EnsureLocalizedStringsLoaded();

            if (string.IsNullOrEmpty(id)) return id;

            var category = LocalizationPluralRules.ResolveCategory(_currentLanguage?.Code, count);
            // 复数回落链的分支候选不算缺译——只有全链（类别→other→裸 key）都落空才计一次
            var text = ResolveRawUntracked(id + "#" + category, _currentLanguage)
                       ?? (category == "other" ? null : ResolveRawUntracked(id + "#other", _currentLanguage))
                       ?? ResolveRawUntracked(id, _currentLanguage);

            if (text == null)
            {
                if (_dataLoaded) TrackMissingKey(id, _currentLanguage);
                return id;
            }

            if (p is not { Length: > 0 })
            {
                try
                {
                    return string.Format(FormatCulture, text, count);
                }
                catch (FormatException)
                {
                    return text;
                }
            }

            // {0} = 数量，调用方参数整体后移一位（一次装箱数组，复数属低频路径，不为省这一次分配把签名复杂化）
            var args = new object[p.Length + 1];
            args[0] = count;
            Array.Copy(p, 0, args, 1, p.Length);

            try
            {
                return string.Format(FormatCulture, text, args);
            }
            catch (FormatException)
            {
                LogFormatError(id, text, args.Length);
                return text;
            }
        }

        /// <summary>
        /// 按「覆盖层 → 指定语言」取原始译文，两处都给不出时返回 <c>null</c>。调用方须已确保数据加载完成。
        /// </summary>
        private string ResolveRaw(string id, Language language)
        {
            // ID 为空、或词条根本不存在时无从取值，一律由调用方露出 ID
            if (string.IsNullOrEmpty(id)) return null;

            var text = ResolveRawUntracked(id, language);
            // 数据未加载时整库为空，「查不到」不等于「缺译」，不记录
            if (text == null && _dataLoaded) TrackMissingKey(id, language);
            return text;
        }

        /// <summary>
        /// 只读解析一条译文：不计缺译、不告警。
        /// </summary>
        /// <remarks>Inspector 预览在重绘路径上，每次重绘都查一遍同一条 key；
        /// 走 <see cref="GetTextFromId(string,object[])"/> 会把编辑器自身的重复查询算进 QA 的缺译计数与事件数。</remarks>
        internal string PeekText(string id, Language language) => ResolveRawUntracked(id, language);

        /// <summary>
        /// 与 <see cref="ResolveRaw"/> 同一条解析路径，但不计缺译——复数回落链的分支候选不命中不算缺译。
        /// </summary>
        private string ResolveRawUntracked(string id, Language language)
        {
            // 当前语言的列下标已在切换时缓存——查询热路径不再每次线性扫语言表
            var index = language == _currentLanguage ? _currentLanguageIndex : Store.IndexOf(language);
            return Store.Resolve(id, language, index);
        }

        /// <summary>
        /// 记录一次缺译：去重进集合、逐 key 告警一次；超容量后仅保留计数。
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
                LogUtility.Warning("Localized text '{0}' is missing for language '{1}'; the key itself is displayed.",
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
        /// <remarks>按语言列模式下会先按需装载全部语言列——这是「全语言」语义的必要代价，热路径请勿使用。</remarks>
        public Dictionary<string, string> GetDictionaryFromId(string id)
        {
            EnsureLocalizedStringsLoaded();

            if (SupportsPerLanguageLoad)
            {
                for (var i = 0; i < Store.LanguageCount; i++)
                {
                    EnsureColumnLoaded(i, required: false);
                }
            }

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
            return Store.GetAllKeys();
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
