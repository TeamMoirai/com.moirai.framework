using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 动态实体持久化核心（internal 静态）：会话生成/销毁表、生成管线（未激活临时父技巧）、模板差分捕获、恢复管线。
    /// <para>外观 <c>SaveService.Entity.cs</c> 经本核心实现；测试经可注入的模板加载器（<see cref="s_TemplateLoaderSync"/>/<see cref="s_TemplateLoaderAsync"/>）
    /// 与 <see cref="ResetForTests"/> 隔离。主线程契约（全部入口均触碰场景对象）。</para>
    /// <para>块布局：实体表 = 保留块 <c>__entities</c>（生成记录 + 预置对象销毁 ID）；实体数据 = 每实体一个 <c>entity:{EntityId}</c> 块
    /// （与预制体模板基准差分后的稀疏 KVT——只写相对模板的变动字段）。CarryForward 语义：保存仅 upsert 活跃实体，
    /// 未访问场景/生成失败实体的块原样滞留；绕过 <c>DestroyPersistent</c> 直接销毁的实体记录与块同样滞留（须走显式销毁移除）。</para>
    /// </summary>
    internal static class SaveEntityPersistence
    {
        /// <summary>实体块键前缀（保留——用户块键不得以此前缀开头，由实体管线独占管理）。</summary>
        internal const string ENTITY_BLOCK_KEY_PREFIX = "entity:";

        /// <summary>实体表保留块键（生成记录 + 销毁 ID；经原始块管线读写以豁免保留前缀校验）。</summary>
        internal const string ENTITY_TABLE_BLOCK_KEY = "__entities";

        /// <summary>克隆名后缀（实例化命名规整用）。</summary>
        private const string CLONE_NAME_SUFFIX = "(Clone)";

        /// <summary>会话生成表（顺序保持；恢复后以档案状态整体替换）。</summary>
        private static readonly List<SaveSpawnRecord> s_Spawns = new List<SaveSpawnRecord>();

        /// <summary>会话生成 ID 集（查重）。</summary>
        private static readonly HashSet<string> s_SpawnIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>会话销毁 ID 列表（预置对象；有序）。</summary>
        private static readonly List<string> s_DestroyedIds = new List<string>();

        /// <summary>会话销毁 ID 集（查重）。</summary>
        private static readonly HashSet<string> s_DestroyedIdSet = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>模板基准缓存（prefabKey → 基准 KVT；会话级——预制体内容在会话内不变）。</summary>
        private static readonly Dictionary<string, byte[]> s_TemplateBaselines = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>增量保存基准表（按档键控 <c>folderName/fileName</c> → 上次保存状态；会话级——进程重启自然清空走全量）。</summary>
        private static readonly Dictionary<string, LastSavedState> s_LastSavedStates = new Dictionary<string, LastSavedState>(StringComparer.Ordinal);

        /// <summary>陈旧块键解析器（全量合并的读档后删除集委托——静态方法组缓存，免每次委托分配）。</summary>
        internal static readonly Func<Dictionary<string, byte[]>, List<string>> StaleKeyResolver = ComputeStaleEntityKeys;

        /// <summary>
        /// 上次保存状态（增量判定基准：实体表字节 + 逐实体差分字节 + 档写入时间）。
        /// </summary>
        private sealed class LastSavedState
        {
            /// <summary>实体表块基准字节。</summary>
            internal byte[] TableBytes;

            /// <summary>逐实体差分基准（entityId → 差分载荷）。</summary>
            internal Dictionary<string, byte[]> DiffPayloads;

            /// <summary>保存完成后的档写入时间（外部删档/改写守卫）。</summary>
            internal DateTime FileWriteTimeUtc;
        }

        /// <summary>未激活临时父物体（阻断 Awake：先注入 ID/块键，就位后再激活注册）。</summary>
        private static GameObject s_Staging;

        /// <summary>同步模板加载器（prefabKey → 未激活克隆，调用方接管生命周期；默认经 ResourceService + SavePrefabRegistry）。</summary>
        internal static Func<string, GameObject> s_TemplateLoaderSync;

        /// <summary>异步模板加载器（同上语义）。</summary>
        internal static Func<string, CancellationToken, UniTask<GameObject>> s_TemplateLoaderAsync;

        /// <summary>
        /// 静态构造：接线默认模板加载器（ResourceService 加载 + SavePrefabRegistry 解析）。
        /// </summary>
        static SaveEntityPersistence()
        {
            WireDefaultLoaders();
        }

        /// <summary>
        /// 静态状态复位（无域重载进入播放时清理会话状态与临时父物体，保证跨会话无脏状态）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Spawns.Clear();
            s_SpawnIds.Clear();
            s_DestroyedIds.Clear();
            s_DestroyedIdSet.Clear();
            s_TemplateBaselines.Clear();
            s_LastSavedStates.Clear();
            if (s_Staging != null)
            {
                DestroyObject(s_Staging);
                s_Staging = null;
            }
        }

        /// <summary>
        /// 测试隔离复位（清空会话状态并恢复默认加载器）。
        /// </summary>
        internal static void ResetForTests()
        {
            ResetStatics();
            WireDefaultLoaders();
        }

        /// <summary>
        /// 接线默认模板加载器。
        /// </summary>
        private static void WireDefaultLoaders()
        {
            s_TemplateLoaderSync = LoadTemplateSyncDefault;
            s_TemplateLoaderAsync = LoadTemplateAsyncDefault;
        }

        #region 生成与销毁 [SPAWN / DESTROY]

        /// <summary>
        /// 判定块键是否由实体管线管理（<c>entity:</c> 前缀——组件存取管线据此跳过实体块）。
        /// </summary>
        /// <param name="key">块键。</param>
        /// <returns>实体块键返回 <c>true</c>。</returns>
        internal static bool IsEntityBlockKey(string key)
        {
            return key != null && key.StartsWith(ENTITY_BLOCK_KEY_PREFIX, StringComparison.Ordinal);
        }

        /// <summary>
        /// 拼装实体块键。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <returns>实体块键。</returns>
        internal static string BuildEntityBlockKey(string entityId)
        {
            return StringUtility.Concat(ENTITY_BLOCK_KEY_PREFIX, entityId);
        }

        /// <summary>
        /// 确保未激活临时父物体存在（懒创建；隐藏不保存，播放态跨场景常驻）。
        /// </summary>
        /// <returns>临时父物体。</returns>
        internal static GameObject EnsureStaging()
        {
            if (s_Staging == null)
            {
                s_Staging = new GameObject("SaveEntityStaging");
                s_Staging.SetActive(false);
                s_Staging.hideFlags = HideFlags.HideAndDontSave;
                if (Application.isPlaying)
                {
                    UObject.DontDestroyOnLoad(s_Staging);
                }
            }

            return s_Staging;
        }

        /// <summary>
        /// 生成核心：接管模板克隆（staging 下未激活实例），注入实体身份与块键，就位后激活并登记会话生成表。
        /// <para>未激活期完成全部就位（父级/场景/变换）——激活触发的 Awake 即见最终状态。</para>
        /// </summary>
        /// <param name="template">模板克隆（staging 下未激活；本方法接管其生命周期）。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="entityId">实体稳定标识（<c>null</c> = 新 GUID，新生成路径）。</param>
        /// <param name="sceneName">落位场景名（空/未加载 = 活跃场景；父级非空时随父级场景）。</param>
        /// <param name="position">世界坐标（<c>null</c> = 保持模板变换，恢复路径）。</param>
        /// <param name="rotation">世界旋转（<paramref name="position"/> 为 <c>null</c> 时忽略）。</param>
        /// <param name="parent">父级（<c>null</c> = 场景根）。</param>
        /// <param name="attributionSceneName">归属场景名（<c>null</c> = 实际落位场景；恢复路径传入原记录场景——落位回落不改写归属，跨会话不漂移）。</param>
        /// <returns>激活后的实体实例；<paramref name="template"/> 为 <c>null</c> 时返回 <c>null</c>。</returns>
        internal static GameObject SpawnCore(GameObject template, string prefabKey, string entityId, string sceneName, Vector3? position, Quaternion? rotation, Transform parent, string attributionSceneName = null)
        {
            if (template == null)
            {
                return null;
            }

            entityId ??= Guid.NewGuid().ToString("N");

            SaveObjectIdentity identity = template.GetComponent<SaveObjectIdentity>();
            if (identity == null)
            {
                identity = template.AddComponent<SaveObjectIdentity>();
            }

            identity.InjectRuntimeId(entityId);

            SaveComponent saveComponent = template.GetComponent<SaveComponent>();
            if (saveComponent != null)
            {
                saveComponent.BlockKey = BuildEntityBlockKey(entityId);
            }

            // 名称规整：去 "(Clone)" 后缀（持久化路径派生与层级可读性）
            string baseName = template.name;
            if (baseName.EndsWith(CLONE_NAME_SUFFIX, StringComparison.Ordinal))
            {
                template.name = baseName.Substring(0, baseName.Length - CLONE_NAME_SUFFIX.Length);
            }

            Transform templateTransform = template.transform;
            // 先脱离 staging（SetParent(null) 成为场景根——MoveGameObjectToScene 仅接受根物体）
            templateTransform.SetParent(parent, false);
            if (parent == null)
            {
                // 根实体场景落位：优先记录场景（已加载时），否则活跃场景
                UnityEngine.SceneManagement.Scene targetScene = ResolveSpawnScene(sceneName);
                if (template.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(template, targetScene);
                }
            }

            if (position.HasValue)
            {
                templateTransform.SetPositionAndRotation(position.Value, rotation ?? Quaternion.identity);
            }

            ActivateEntity(template);

            string parentId = null;
            if (parent != null)
            {
                SaveObjectIdentity parentIdentity = SaveObjectIdentity.Resolve(parent);
                if (parentIdentity != null && !string.IsNullOrEmpty(parentIdentity.Id))
                {
                    parentId = parentIdentity.Id;
                }
                else
                {
                    LogUtility.Warning("[SaveService] InstantiatePersistent parent '{0}' has no SaveObjectIdentity — parent link will not persist.", parent.name);
                }
            }

            // 归属场景优先取调用方指定值（恢复路径 = 档案原记录）：落位回落到活跃场景不改写归属，跨会话不漂移
            string recordedSceneName = string.IsNullOrEmpty(attributionSceneName) ? template.scene.name : attributionSceneName;
            RegisterSpawn(new SaveSpawnRecord(entityId, prefabKey, recordedSceneName, parentId));
            return template;
        }

        /// <summary>
        /// 销毁核心：动态实体从生成表移除（其块在下次保存时按陈旧清理）；预置对象记入销毁表（恢复时DestroyUnwanted）。
        /// </summary>
        /// <param name="target">目标物体（<c>null</c> 静默忽略）。</param>
        internal static void DestroyCore(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            SaveObjectIdentity identity = SaveObjectIdentity.Resolve(target);
            if (identity != null && !string.IsNullOrEmpty(identity.Id))
            {
                if (s_SpawnIds.Contains(identity.Id))
                {
                    UnregisterSpawn(identity.Id);
                }
                else if (s_DestroyedIdSet.Add(identity.Id))
                {
                    s_DestroyedIds.Add(identity.Id);
                }
            }

            DestroyObject(target);
        }

        /// <summary>
        /// 登记生成记录（重复 ID 首到先得并记告警）。
        /// </summary>
        /// <param name="record">生成记录。</param>
        private static void RegisterSpawn(SaveSpawnRecord record)
        {
            if (!s_SpawnIds.Add(record.EntityId))
            {
                LogUtility.Warning("[SaveService] Duplicate spawn entity id '{0}' (prefab '{1}') — first record wins.", record.EntityId, record.PrefabKey);
                return;
            }

            s_Spawns.Add(record);
        }

        /// <summary>
        /// 移除生成记录。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        private static void UnregisterSpawn(string entityId)
        {
            if (!s_SpawnIds.Remove(entityId))
            {
                return;
            }

            for (int i = 0; i < s_Spawns.Count; i++)
            {
                if (string.Equals(s_Spawns[i].EntityId, entityId, StringComparison.Ordinal))
                {
                    s_Spawns.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 激活实体并完成注册（编辑模式兜底：非 ExecuteInEditMode 组件的 Awake 在编辑模式不执行，
        /// 此处显式补齐——播放态 Awake 已执行，注册与 EnsureActivated 均幂等无副作用）。
        /// </summary>
        /// <param name="instance">实体实例（调用前应保持未激活的全部就位已完成）。</param>
        private static void ActivateEntity(GameObject instance)
        {
            instance.SetActive(true);
            SaveObjectIdentity identity = instance.GetComponent<SaveObjectIdentity>();
            if (identity != null)
            {
                SaveEntityRegistry.Register(identity);
            }

            SaveComponent saveComponent = instance.GetComponent<SaveComponent>();
            if (saveComponent != null)
            {
                saveComponent.EnsureActivated();
            }
        }

        /// <summary>
        /// 解析生成落位场景（记录场景已加载则落位其中，否则活跃场景并记告警）。
        /// </summary>
        /// <param name="sceneName">场景名（空 = 活跃场景）。</param>
        /// <returns>落位场景。</returns>
        private static UnityEngine.SceneManagement.Scene ResolveSpawnScene(string sceneName)
        {
            if (!string.IsNullOrEmpty(sceneName))
            {
                UnityEngine.SceneManagement.Scene recorded = SceneManager.GetSceneByName(sceneName);
                if (recorded.IsValid() && recorded.isLoaded)
                {
                    return recorded;
                }

                LogUtility.Warning("[SaveService] Spawn scene '{0}' is not loaded — spawning into the active scene '{1}'.", sceneName, SceneManager.GetActiveScene().name);
            }

            return SceneManager.GetActiveScene();
        }

        #endregion

        #region 模板基准 [TEMPLATE BASELINE]

        /// <summary>
        /// 获取预制体模板基准捕获（会话级缓存；缺失时经同步加载器现捕）。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <returns>基准 KVT 字节；加载器不可用/预制体无 SaveComponent 时返回 <c>null</c>（差分退化为全量/跳过）。</returns>
        internal static byte[] GetBaseline(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey))
            {
                return null;
            }

            if (s_TemplateBaselines.TryGetValue(prefabKey, out byte[] cached))
            {
                return cached;
            }

            if (s_TemplateLoaderSync == null)
            {
                return null;
            }

            GameObject template = s_TemplateLoaderSync(prefabKey);
            return CaptureBaselineFromTemplate(prefabKey, template);
        }

        /// <summary>
        /// 异步预热全部缺失的模板基准（保存前调用，避免捕获期同步加载卡顿）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>预热完成的异步任务。</returns>
        internal static async UniTask WarmBaselinesAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < s_Spawns.Count; i++)
            {
                string prefabKey = s_Spawns[i].PrefabKey;
                if (string.IsNullOrEmpty(prefabKey) || s_TemplateBaselines.ContainsKey(prefabKey))
                {
                    continue;
                }

                if (s_TemplateLoaderAsync != null)
                {
                    GameObject template = await s_TemplateLoaderAsync(prefabKey, cancellationToken);
                    CaptureBaselineFromTemplate(prefabKey, template);
                }
                else
                {
                    GetBaseline(prefabKey);
                }
            }
        }

        /// <summary>
        /// 从模板克隆捕获基准 KVT（未激活实例——Awake 未运行的纯净模板态），捕获后销毁克隆并缓存。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="template">模板克隆（可为 <c>null</c>——加载失败）。</param>
        /// <returns>基准 KVT 字节；无根 SaveComponent 或克隆缺失时 <c>null</c>。</returns>
        private static byte[] CaptureBaselineFromTemplate(string prefabKey, GameObject template)
        {
            if (template == null)
            {
                return null;
            }

            try
            {
                SaveComponent saveComponent = template.GetComponent<SaveComponent>();
                if (saveComponent == null)
                {
                    LogUtility.Warning("[SaveService] Prefab '{0}' has no SaveComponent on its root — entity diff baseline unavailable, capturing in full.", prefabKey);
                    return null;
                }

                var writer = new SaveKeyValueWriter(256);
                saveComponent.Capture(ref writer);
                byte[] baseline = writer.ToArray();
                s_TemplateBaselines[prefabKey] = baseline;
                return baseline;
            }
            finally
            {
                DestroyObject(template);
            }
        }

        #endregion

        #region 捕获 [CAPTURE]

        /// <summary>
        /// 捕获实体差分块与实体表块（主线程；按批触发保存进度事件）。
        /// <para>活跃实体逐只全量捕获后与模板基准差分；无基准（加载器缺失/预制体无 SaveComponent）退化为全量写入。
        /// 不在册实体（已被绕过 DestroyPersistent 销毁）跳过捕获但保留其记录（CarryForward——原块滞留）。</para>
        /// </summary>
        /// <param name="fileName">存档文件名（进度事件参数）。</param>
        /// <param name="folderName">存档文件夹名称（进度事件参数）。</param>
        /// <returns>块条目列表（末位恒为实体表块）。</returns>
        internal static List<SaveBlockEntry> CaptureEntityEntries(string fileName, string folderName)
        {
            var entries = new List<SaveBlockEntry>(s_Spawns.Count + 1);
            int total = s_Spawns.Count;
            for (int i = 0; i < s_Spawns.Count; i++)
            {
                SaveSpawnRecord record = s_Spawns[i];
                if (SaveEntityRegistry.TryFind(record.EntityId, out SaveObjectIdentity identity) && identity != null)
                {
                    SaveComponent saveComponent = identity.GetComponent<SaveComponent>();
                    if (saveComponent != null)
                    {
                        var writer = new SaveKeyValueWriter(256);
                        saveComponent.Capture(ref writer);
                        byte[] full = writer.ToArray();
                        byte[] baseline = GetBaseline(record.PrefabKey);
                        byte[] payload = SaveKvDiffer.Diff(baseline, full);
                        entries.Add(new SaveBlockEntry(BuildEntityBlockKey(record.EntityId), 1, ESaveBackend.KeyValue, payload));
                    }
                }

                int completed = i + 1;
                if (SaveService.ShouldReportProgress(completed, total))
                {
                    SaveService.RaiseSaveProgress(fileName, folderName, completed, total);
                }
            }

            entries.Add(new SaveBlockEntry(ENTITY_TABLE_BLOCK_KEY, 1, ESaveBackend.KeyValue, SaveEntityTable.Write(s_Spawns, s_DestroyedIds)));
            return entries;
        }

        /// <summary>
        /// 计算陈旧实体块键（档内生成表 − 会话生成表 = 已被 DestroyPersistent 移除的实体；
        /// 以及两表均无登记的孤儿实体块——历史中断写入的残留）。
        /// </summary>
        /// <param name="existingBlocks">档内现有块（键 → 载荷；缺档为空字典）。</param>
        /// <returns>应删除的块键列表。</returns>
        internal static List<string> ComputeStaleEntityKeys(Dictionary<string, byte[]> existingBlocks)
        {
            var staleKeys = new List<string>();
            if (existingBlocks == null || existingBlocks.Count == 0)
            {
                return staleKeys;
            }

            List<SaveSpawnRecord> fileSpawns;
            if (existingBlocks.TryGetValue(ENTITY_TABLE_BLOCK_KEY, out byte[] tableBytes))
            {
                SaveEntityTable.Read(tableBytes, out fileSpawns, out _);
            }
            else
            {
                fileSpawns = null;
            }

            // 档内有记录但会话已移除 → 陈旧
            if (fileSpawns != null)
            {
                for (int i = 0; i < fileSpawns.Count; i++)
                {
                    string entityId = fileSpawns[i].EntityId;
                    if (!string.IsNullOrEmpty(entityId) && !s_SpawnIds.Contains(entityId))
                    {
                        staleKeys.Add(BuildEntityBlockKey(entityId));
                    }
                }
            }

            // 两表均无登记的孤儿实体块 → 清理
            foreach (KeyValuePair<string, byte[]> pair in existingBlocks)
            {
                if (!IsEntityBlockKey(pair.Key))
                {
                    continue;
                }

                string entityId = pair.Key.Substring(ENTITY_BLOCK_KEY_PREFIX.Length);
                if (s_SpawnIds.Contains(entityId))
                {
                    continue;
                }

                if (fileSpawns != null)
                {
                    bool inFile = false;
                    for (int i = 0; i < fileSpawns.Count; i++)
                    {
                        if (string.Equals(fileSpawns[i].EntityId, entityId, StringComparison.Ordinal))
                        {
                            inFile = true;
                            break;
                        }
                    }

                    if (inFile)
                    {
                        continue; // 档内记录在册（陈旧判定已在上方处理）
                    }
                }

                staleKeys.Add(pair.Key);
            }

            return staleKeys;
        }

        /// <summary>
        /// 增量保存判定（会话级脏跟踪 + 档写入时间守卫）：存在有效基准且档未被外部改写时，
        /// 逐实体比对比分载荷与实体表字节——零变化返回 <c>true</c>（调用方零 IO 跳过，替代三趟 IO 的整档读改写）。
        /// <para>基准失效（无记录/档缺失/写入时间失配）返回 <c>false</c> 且 <paramref name="dirtyEntries"/> 为 <c>null</c>——
        /// 调用方按全量模式合并（全部条目 upsert + 孤儿清理由 <see cref="StaleKeyResolver"/> 读档判定）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="entries">本次捕获的全部实体条目（末位恒为实体表块）。</param>
        /// <param name="handler">存档处理器（档写入时间查询）。</param>
        /// <param name="dirtyEntries">需写回的变化条目（跳过/全量模式为 <c>null</c>；增量模式含变化差分块与变化的实体表块）。</param>
        /// <param name="certainRemovals">会话内确定的删除块键（上次在册 − 当前在册；全量模式为 <c>null</c>）。</param>
        /// <returns><c>true</c> = 零变化且档未被外部改写（跳过保存）；<c>false</c> = 需要写回。</returns>
        internal static bool TrySkipEntitySave(SaveServiceHandler.SavePaths paths, List<SaveBlockEntry> entries, SaveServiceHandler handler, out List<SaveBlockEntry> dirtyEntries, out List<string> certainRemovals)
        {
            dirtyEntries = null;
            certainRemovals = null;
            if (!s_LastSavedStates.TryGetValue(BuildStateKey(paths), out LastSavedState state))
            {
                return false;
            }

            // 档写入时间守卫：档缺失或被外部改写（删除重建/备份回滚/云下载刷新镜像）即基准失效——保守走全量
            if (!handler.TryGetSaveWriteTimeUtc(paths, out DateTime writeTimeUtc) || writeTimeUtc != state.FileWriteTimeUtc)
            {
                return false;
            }

            byte[] tableBytes = entries[entries.Count - 1].Bytes;
            bool tableChanged = !tableBytes.AsSpan().SequenceEqual(state.TableBytes);

            dirtyEntries = new List<SaveBlockEntry>();
            for (int i = 0; i < entries.Count - 1; i++)
            {
                SaveBlockEntry entry = entries[i];
                string entityId = entry.Key.Substring(ENTITY_BLOCK_KEY_PREFIX.Length);
                if (!state.DiffPayloads.TryGetValue(entityId, out byte[] lastPayload) || !entry.Bytes.AsSpan().SequenceEqual(lastPayload))
                {
                    dirtyEntries.Add(entry);
                }
            }

            foreach (KeyValuePair<string, byte[]> pair in state.DiffPayloads)
            {
                if (!s_SpawnIds.Contains(pair.Key))
                {
                    (certainRemovals ??= new List<string>()).Add(BuildEntityBlockKey(pair.Key));
                }
            }

            if (tableChanged)
            {
                dirtyEntries.Add(entries[entries.Count - 1]);
            }

            if (dirtyEntries.Count == 0 && certainRemovals == null)
            {
                dirtyEntries = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 提交本次保存为增量基准（merge 成功后调用——下次保存的脏判定基准；基准字节直引条目缓冲，捕获产物不再变更）。
        /// </summary>
        /// <param name="paths">已解析的路径集合。</param>
        /// <param name="entries">本次保存的全部实体条目（末位恒为实体表块）。</param>
        /// <param name="fileWriteTimeUtc">保存完成后的档写入时间（守卫基准）。</param>
        internal static void CommitSavedState(SaveServiceHandler.SavePaths paths, List<SaveBlockEntry> entries, DateTime fileWriteTimeUtc)
        {
            var state = new LastSavedState
            {
                TableBytes = entries[entries.Count - 1].Bytes,
                DiffPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal),
                FileWriteTimeUtc = fileWriteTimeUtc,
            };
            for (int i = 0; i < entries.Count - 1; i++)
            {
                string entityId = entries[i].Key.Substring(ENTITY_BLOCK_KEY_PREFIX.Length);
                state.DiffPayloads[entityId] = entries[i].Bytes;
            }

            s_LastSavedStates[BuildStateKey(paths)] = state;
        }

        /// <summary>
        /// 失效全部增量基准（恢复管线替换会话状态后调用——下次保存走全量，对齐恢复前行为）。
        /// </summary>
        internal static void InvalidateSavedStates()
        {
            s_LastSavedStates.Clear();
        }

        /// <summary>
        /// 构造增量基准键（folderName/fileName——同档同名文件夹唯一键控）。
        /// </summary>
        private static string BuildStateKey(SaveServiceHandler.SavePaths paths)
        {
            return StringUtility.Concat(paths.FolderName, "/", paths.FileName);
        }

        #endregion

        #region 恢复 [RESTORE]

        /// <summary>
        /// 恢复管线：DestroyUnwanted（会话实体整体替换 + 销毁表预置对象）→ SpawnMissing（未激活生成 + 场景落位）
        /// → 父子接线（第二轮）→ RestoreAll（差分块未激活写回，Awake 见恢复后状态）→ 激活 + 实体恢复事件。
        /// <para>会话生成/销毁表以档案状态整体替换（CarryForward 基准）；生成失败的实体保留其档案记录（原块滞留，下次保存不丢）。</para>
        /// </summary>
        /// <param name="blocks">档内全部健康块（键 → 载荷）。</param>
        /// <param name="fileName">存档文件名（进度事件参数）。</param>
        /// <param name="folderName">存档文件夹名称（进度事件参数）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>恢复完成的异步任务。</returns>
        internal static async UniTask RestoreFromBlocksAsync(Dictionary<string, byte[]> blocks, string fileName, string folderName, CancellationToken cancellationToken)
        {
            blocks = blocks ?? new Dictionary<string, byte[]>();
            blocks.TryGetValue(ENTITY_TABLE_BLOCK_KEY, out byte[] tableBytes);
            SaveEntityTable.Read(tableBytes, out List<SaveSpawnRecord> fileSpawns, out List<string> fileDestroyed);

            // —— DestroyUnwanted：会话生成实体整体移除（档案状态替换会话状态）
            for (int i = 0; i < s_Spawns.Count; i++)
            {
                if (SaveEntityRegistry.TryFind(s_Spawns[i].EntityId, out SaveObjectIdentity active) && active != null)
                {
                    DestroyObject(active.gameObject);
                }
            }

            s_Spawns.Clear();
            s_SpawnIds.Clear();

            // —— DestroyUnwanted：销毁表内的预置对象
            for (int i = 0; i < fileDestroyed.Count; i++)
            {
                if (SaveEntityRegistry.TryFind(fileDestroyed[i], out SaveObjectIdentity doomed) && doomed != null)
                {
                    DestroyObject(doomed.gameObject);
                }
            }

            // —— SpawnMissing 第一轮：加载模板 + 未激活生成 + 场景落位（生成失败保留档案记录）
            var pending = new List<PendingRestore>(fileSpawns.Count);
            for (int i = 0; i < fileSpawns.Count; i++)
            {
                SaveSpawnRecord record = fileSpawns[i];
                if (string.IsNullOrEmpty(record.EntityId) || string.IsNullOrEmpty(record.PrefabKey))
                {
                    LogUtility.Warning("[SaveService] Malformed spawn record in entity table (id '{0}', prefab '{1}'), skipped.", record.EntityId, record.PrefabKey);
                    continue;
                }

                GameObject template = await LoadTemplateAsync(record.PrefabKey, cancellationToken);
                if (template == null)
                {
                    LogUtility.Error("[SaveService] Failed to load prefab '{0}' for entity '{1}' — record preserved, spawn skipped.", record.PrefabKey, record.EntityId);
                    RegisterSpawn(record);
                    continue;
                }

                // 撞 ID 的活跃对象（非会话登记——预置烘焙撞键等）先移除，避免重复 ID 告警与双实例
                if (SaveEntityRegistry.TryFind(record.EntityId, out SaveObjectIdentity existing) && existing != null)
                {
                    DestroyObject(existing.gameObject);
                }

                GameObject instance = SpawnCore(template, record.PrefabKey, record.EntityId, record.SceneName, null, null, null, record.SceneName);
                pending.Add(new PendingRestore(record, instance));

                int completed = i + 1;
                if (SaveService.ShouldReportProgress(completed, fileSpawns.Count))
                {
                    SaveService.RaiseLoadProgress(fileName, folderName, completed, fileSpawns.Count);
                }
            }

            // —— 第二轮：父子接线（父级可能是后生成的实体，故独立一轮；未激活状态 SetParent 不触发 Awake）
            for (int i = 0; i < pending.Count; i++)
            {
                PendingRestore entry = pending[i];
                if (string.IsNullOrEmpty(entry.Record.ParentId))
                {
                    continue;
                }

                if (SaveEntityRegistry.TryFind(entry.Record.ParentId, out SaveObjectIdentity parentIdentity) && parentIdentity != null)
                {
                    entry.Instance.transform.SetParent(parentIdentity.transform, false);
                }
                else
                {
                    LogUtility.Warning("[SaveService] Parent id '{0}' of entity '{1}' not found — restoring as scene root.", entry.Record.ParentId, entry.Record.EntityId);
                }
            }

            // —— 第三轮：差分块写回（未激活状态——Awake 即见恢复后字段值）
            ApplyEntityDiffBlocks(pending, blocks);

            // —— 激活 + 实体恢复事件（Awake/OnEnable 见最终父级与恢复后状态）
            for (int i = 0; i < pending.Count; i++)
            {
                PendingRestore entry = pending[i];
                ActivateEntity(entry.Instance);
                SaveService.RaiseEntityRestored(entry.Record.EntityId, entry.Record.PrefabKey, entry.Instance);
            }

            // —— 销毁表以档案状态替换
            s_DestroyedIds.Clear();
            s_DestroyedIdSet.Clear();
            for (int i = 0; i < fileDestroyed.Count; i++)
            {
                if (s_DestroyedIdSet.Add(fileDestroyed[i]))
                {
                    s_DestroyedIds.Add(fileDestroyed[i]);
                }
            }

            // 恢复替换会话状态——增量基准全部失效（下次保存走全量，与恢复前写回行为对齐）
            InvalidateSavedStates();
        }

        /// <summary>
        /// 待恢复实体（第一轮生成的暂存）。
        /// </summary>
        private readonly struct PendingRestore
        {
            /// <summary>生成记录。</summary>
            internal readonly SaveSpawnRecord Record;

            /// <summary>未激活实例。</summary>
            internal readonly GameObject Instance;

            internal PendingRestore(SaveSpawnRecord record, GameObject instance)
            {
                Record = record;
                Instance = instance;
            }
        }

        /// <summary>
        /// 第三轮差分块写回（非 async 方法：SaveKeyValueReader 为 ref struct，不可进 async 上下文）。
        /// <para>缺块实体保留模板默认值；KVT 损坏的实体记录错误日志并跳过（不阻断其它实体）。</para>
        /// </summary>
        /// <param name="pending">待恢复实体列表。</param>
        /// <param name="blocks">档内全部健康块。</param>
        private static void ApplyEntityDiffBlocks(List<PendingRestore> pending, Dictionary<string, byte[]> blocks)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                PendingRestore entry = pending[i];
                if (!blocks.TryGetValue(BuildEntityBlockKey(entry.Record.EntityId), out byte[] diffBytes))
                {
                    continue;
                }

                SaveComponent saveComponent = entry.Instance.GetComponent<SaveComponent>();
                if (saveComponent == null)
                {
                    continue;
                }

                try
                {
                    var reader = new SaveKeyValueReader(diffBytes);
                    saveComponent.Restore(ref reader);
                }
                catch (SaveKvFormatException exception)
                {
                    LogUtility.Error("[SaveService] Entity restore failed, id: {0}, message: {1}.", entry.Record.EntityId, exception.Message);
                }
            }
        }

        /// <summary>
        /// 加载模板（异步加载器优先，同步加载器兜底直转）。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>未激活模板克隆；加载器缺失/加载失败返回 <c>null</c>。</returns>
        private static async UniTask<GameObject> LoadTemplateAsync(string prefabKey, CancellationToken cancellationToken)
        {
            if (s_TemplateLoaderAsync != null)
            {
                return await s_TemplateLoaderAsync(prefabKey, cancellationToken);
            }

            return s_TemplateLoaderSync?.Invoke(prefabKey);
        }

        #endregion

        #region 默认加载器 [DEFAULT LOADERS]

        /// <summary>
        /// 默认同步模板加载：SavePrefabRegistry 解析定位串 → ResourceService 加载到 staging 下（未激活）。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <returns>未激活模板克隆；注册表未配置/键未登记/加载失败返回 <c>null</c>（均已记录错误日志）。</returns>
        private static GameObject LoadTemplateSyncDefault(string prefabKey)
        {
            if (!TryResolvePrefabLocation(prefabKey, out string location))
            {
                return null;
            }

            GameObject instance = Resource.ResourceService.LoadGameObject(location, EnsureStaging().transform);
            if (instance == null)
            {
                LogUtility.Error("[SaveService] ResourceService failed to load prefab '{0}' (location '{1}').", prefabKey, location);
                return null;
            }

            return instance;
        }

        /// <summary>
        /// 默认异步模板加载（语义同 <see cref="LoadTemplateSyncDefault"/>）。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>未激活模板克隆；失败返回 <c>null</c>。</returns>
        private static async UniTask<GameObject> LoadTemplateAsyncDefault(string prefabKey, CancellationToken cancellationToken)
        {
            if (!TryResolvePrefabLocation(prefabKey, out string location))
            {
                return null;
            }

            GameObject instance = await Resource.ResourceService.LoadGameObjectAsync(location, EnsureStaging().transform, cancellationToken);
            if (instance == null)
            {
                LogUtility.Error("[SaveService] ResourceService failed to load prefab '{0}' (location '{1}').", prefabKey, location);
                return null;
            }

            return instance;
        }

        /// <summary>
        /// 解析预制体定位串（注册表未配置/键未登记均记错误日志）。
        /// </summary>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="location">定位串。</param>
        /// <returns>解析成功返回 <c>true</c>。</returns>
        private static bool TryResolvePrefabLocation(string prefabKey, out string location)
        {
            SavePrefabRegistry registry = SaveServiceSettings.PrefabRegistry;
            if (registry == null)
            {
                LogUtility.Error("[SaveService] SavePrefabRegistry is not configured in SaveServiceSettings — entity persistence unavailable.");
                location = null;
                return false;
            }

            if (!registry.TryGetLocation(prefabKey, out location))
            {
                LogUtility.Error("[SaveService] Prefab key '{0}' is not registered in SavePrefabRegistry '{1}'.", prefabKey, registry.name);
                return false;
            }

            return true;
        }

        #endregion

        #region 对象销毁 [OBJECT DESTRUCTION]

        /// <summary>
        /// 销毁 Unity 对象（编辑模式直毁，播放模式延迟销毁）。
        /// </summary>
        /// <param name="target">目标对象。</param>
        private static void DestroyObject(UObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UObject.Destroy(target);
            }
            else
            {
                UObject.DestroyImmediate(target);
            }
        }

        #endregion
    }
}
