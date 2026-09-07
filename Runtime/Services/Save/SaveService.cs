using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务外观（Facade）。
    /// <para>统一的静态存档访问入口。存档文件为「单文件多数据块」容器：一个文件（存档槽）内含多个按键寻址的数据块，
    /// 块级 API（<c>SaveBlockAsync</c>/<c>LoadBlockAsync</c>/…）为主体；旧单对象 API（<c>SaveAsync</c>/<c>LoadAsync</c>/…）保留签名并映射到保留块 <see cref="MainBlockKey"/>。</para>
    /// <para>序列化后端（JSON/MessagePack/MemoryPack/protobuf-net）与存储管线（明文/AES 加密）两轴可插拔，经 <see cref="SaveServiceSettings"/> 配置。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="SaveServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(SaveServiceHandler))]
    public partial class SaveService : ServiceBase
    {
        /// <summary>兼容保留块键：旧单对象 API 读写的逻辑数据块。</summary>
        public const string MainBlockKey = SaveServiceHandler.MainBlockKey;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="SaveServiceSettings"/> 创建默认存档处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认存档处理器实例。</returns>
        private static SaveServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<SaveService>();
            return SaveServiceSettings.SaveServiceHandler;
        }

        /// <summary>
        /// 初始化存档服务。由容器在构建期调用。
        /// <para>确保 <c>SaveService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            // 确保 Handler 已初始化（加密处理器在此阶段注入密钥与派生参数）
            _ = Handler;
        }

        /// <summary>
        /// 关闭存档服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        #endregion

        #region 块级读写 [BLOCK SAVE / LOAD]

        /// <summary>
        /// 将数据块异步写入存档文件（读-改-写合并进既有块集合；原子替换；同文件写路径串行排队）。
        /// <para>失败抛出 <see cref="GameException"/>（含路径上下文）；处理器未就绪时静默降级为空任务。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（保留前缀 <c>__</c> 不可用）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            ESaveBackend backend = ResolveBackend<T>();
            return s_Handler is null
                ? UniTask.CompletedTask
                : s_Handler.SaveBlockAsync(data, fileName, key, folderName, backend, ResolveDataVersion<T>(), cancellationToken);
        }

        /// <summary>
        /// 从存档文件异步加载指定数据块。
        /// <para>文件/块不存在或加载失败（损坏/解密失败/反序列化失败，均已记录错误日志）返回 <c>default</c>——需要错误判别时使用 <see cref="TryLoadBlockAsync{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>反序列化后的块数据；失败返回默认值。</returns>
        public static UniTask<T> LoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.LoadBlockAsync<T>(fileName, key, folderName, cancellationToken) ?? UniTask.FromResult<T>(default);

        /// <summary>
        /// 从存档文件异步加载指定数据块并返回完整错误判别结果。
        /// <para>处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/> 失败结果。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>加载结果（区分无档/无块/损坏/解密失败等错误类别）。</returns>
        public static UniTask<SaveResult<T>> TryLoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.TryLoadBlockAsync<T>(fileName, key, folderName, cancellationToken) ?? UniTask.FromResult(SaveResult<T>.Failure(SaveError.HandlerNotReady));

        /// <summary>
        /// 将数据块写入存档文件（在调用线程执行完整管线，阻塞直至完成；处理器未就绪时静默降级为空操作）。
        /// <para>仅限主线程调用；适用于退出前落盘等必须同步完成的场景，大数据量请用 <see cref="SaveBlockAsync{T}"/> 避免阻塞。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="data">存档数据对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键（保留前缀 <c>__</c> 不可用）。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void SaveBlock<T>(T data, string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME)
        {
            if (s_Handler is null)
            {
                return;
            }

            s_Handler.SaveBlock(data, fileName, key, folderName, ResolveBackend<T>(), ResolveDataVersion<T>());
        }

        /// <summary>
        /// 从存档文件加载指定数据块（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；文件/块不存在或加载失败（均已记录错误日志）返回 <c>default</c>，处理器未就绪时同样降级返回 <c>default</c>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>反序列化后的块数据；失败返回默认值。</returns>
        public static T LoadBlock<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler != null ? s_Handler.LoadBlock<T>(fileName, key, folderName) : default;

        /// <summary>
        /// 从存档文件加载指定数据块并返回完整错误判别结果（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/> 失败结果。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>加载结果（区分无档/无块/损坏/解密失败等错误类别）。</returns>
        public static SaveResult<T> TryLoadBlock<T>(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.TryLoadBlock<T>(fileName, key, folderName) ?? SaveResult<T>.Failure(SaveError.HandlerNotReady);

        /// <summary>
        /// 从存档文件中删除指定数据块（删除最后一个块时整档移除），IO 在工作线程执行。
        /// <para>失败抛出 <see cref="GameException"/>；处理器未就绪时静默降级为空任务。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>删除完成的异步任务。</returns>
        public static UniTask DeleteBlockAsync(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.DeleteBlockAsync(fileName, key, folderName, cancellationToken) ?? UniTask.CompletedTask;

        /// <summary>
        /// 从存档文件中删除指定数据块（在调用线程执行，阻塞直至完成；处理器未就绪时静默降级为空操作）。
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="key">数据块键。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void DeleteBlock(string fileName, string key, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DeleteBlock(fileName, key, folderName);

        /// <summary>
        /// 枚举存档文件内的全部数据块（含保留块；同步执行，加密处理器下需解密整档）。
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>块元信息数组；缺档/损坏/处理器未就绪时为空数组（损坏已记录错误日志）。</returns>
        public static SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.GetBlockInfos(fileName, folderName) ?? Array.Empty<SaveBlockInfo>();

        #endregion

        #region 兼容读写 [LEGACY SAVE / LOAD]

        /// <summary>
        /// 将存档对象异步写入磁盘（映射到保留块 <see cref="MainBlockKey"/> 的块写入），IO 在工作线程执行。
        /// <para>失败抛出 <see cref="GameException"/>（含路径上下文）；处理器未就绪时静默降级为空任务。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="saveObject">存档对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static UniTask SaveAsync<T>(T saveObject, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            SaveBlockAsync(saveObject, fileName, MainBlockKey, folderName, cancellationToken);

        /// <summary>
        /// 从磁盘异步加载存档（映射到保留块 <see cref="MainBlockKey"/> 的块读取），IO 在工作线程执行。
        /// <para>文件不存在或加载失败（损坏/解密失败/反序列化失败，均已记录错误日志）返回 <c>default</c>——需要错误判别时使用 <see cref="TryLoadAsync{T}"/>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>反序列化后的存档对象；失败返回默认值。</returns>
        public static UniTask<T> LoadAsync<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            LoadBlockAsync<T>(fileName, MainBlockKey, folderName, cancellationToken);

        /// <summary>
        /// 从磁盘异步加载存档并返回完整错误判别结果，IO 在工作线程执行。
        /// <para>处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/> 失败结果。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>加载结果（区分无档/损坏/解密失败等错误类别）。</returns>
        public static UniTask<SaveResult<T>> TryLoadAsync<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            TryLoadBlockAsync<T>(fileName, MainBlockKey, folderName, cancellationToken);

        /// <summary>
        /// 将存档对象写入磁盘（映射到保留块 <see cref="MainBlockKey"/> 的块写入；在调用线程执行完整管线，阻塞直至完成）。
        /// <para>仅限主线程调用；适用于退出前落盘等必须同步完成的场景，大数据量请用 <see cref="SaveAsync{T}"/> 避免阻塞。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="saveObject">存档对象。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void Save<T>(T saveObject, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            SaveBlock(saveObject, fileName, MainBlockKey, folderName);

        /// <summary>
        /// 从磁盘加载存档（映射到保留块 <see cref="MainBlockKey"/> 的块读取；在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；文件不存在或加载失败（均已记录错误日志）返回 <c>default</c>，处理器未就绪时同样降级返回 <c>default</c>。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>反序列化后的存档对象；失败返回默认值。</returns>
        public static T Load<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            LoadBlock<T>(fileName, MainBlockKey, folderName);

        /// <summary>
        /// 从磁盘加载存档并返回完整错误判别结果（在调用线程执行，阻塞直至完成）。
        /// <para>仅限主线程调用；处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/> 失败结果。</para>
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>加载结果（区分无档/损坏/解密失败等错误类别）。</returns>
        public static SaveResult<T> TryLoad<T>(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            TryLoadBlock<T>(fileName, MainBlockKey, folderName);

        #endregion

        #region 组件存取 [COMPONENTS]

        /// <summary>
        /// 将全部已注册 <see cref="SaveComponent"/> 的勾选字段异步写入存档文件（每组件一个 KVT 块；组件捕获在主线程，合并写回在工作线程）。
        /// <para>失败抛出 <see cref="GameException"/>；处理器未就绪时静默降级为空任务；重复块键的组件记录告警并跳过。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static UniTask SaveComponentsAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            if (s_Handler is null)
            {
                return UniTask.CompletedTask;
            }

            List<SaveBlockEntry> entries = CaptureComponentsToEntries();
            if (entries.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);
            return s_Handler.UpsertRawBlocksAsync(paths, entries, cancellationToken);
        }

        /// <summary>
        /// 从存档文件异步恢复全部已注册 <see cref="SaveComponent"/> 的勾选字段（读盘在工作线程，字段写回在主线程）。
        /// <para>缺块组件保留当前值；KVT 损坏的组件记录错误日志并跳过（不阻断其它组件）。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>恢复完成的异步任务。</returns>
        public static async UniTask LoadComponentsAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            if (s_Handler is null)
            {
                return;
            }

            SaveComponent[] components = SaveComponentRegistry.Snapshot();
            if (components.Length == 0)
            {
                return;
            }

            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);
            Dictionary<string, byte[]> blocks = await s_Handler.ReadRawBlocksAsync(paths, cancellationToken);

            // 主线程写回组件字段（blocks 已在工作线程解析完毕；ref struct 读取器不可进 async 上下文，故收敛到独立方法）
            RestoreComponentsOnMainThread(components, blocks);
        }

        /// <summary>
        /// 在主线程将块字节恢复到组件字段（非 async 方法：SaveKeyValueReader 为 ref struct）。
        /// </summary>
        /// <param name="components">活跃组件快照。</param>
        /// <param name="blocks">块键 → 载荷字节。</param>
        private static void RestoreComponentsOnMainThread(SaveComponent[] components, Dictionary<string, byte[]> blocks)
        {
            for (int i = 0; i < components.Length; i++)
            {
                SaveComponent component = components[i];
                if (component == null || string.IsNullOrEmpty(component.ResolvedBlockKey))
                {
                    continue;
                }

                if (!blocks.TryGetValue(component.ResolvedBlockKey, out byte[] bytes))
                {
                    continue;
                }

                try
                {
                    var reader = new SaveKeyValueReader(bytes);
                    component.Restore(ref reader);
                }
                catch (SaveKvFormatException exception)
                {
                    LogUtility.Error("[SaveService] Component restore failed, key: {0}, message: {1}.", component.ResolvedBlockKey, exception.Message);
                }
            }
        }

        /// <summary>
        /// 捕获全部活跃组件为块条目（主线程；重复块键记录告警并跳过）。
        /// </summary>
        /// <returns>块条目列表（可能为空）。</returns>
        private static List<SaveBlockEntry> CaptureComponentsToEntries()
        {
            SaveComponent[] components = SaveComponentRegistry.Snapshot();
            var entries = new List<SaveBlockEntry>(components.Length);
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < components.Length; i++)
            {
                SaveComponent component = components[i];
                if (component == null)
                {
                    continue;
                }

                string blockKey = component.ResolvedBlockKey;
                if (string.IsNullOrEmpty(blockKey))
                {
                    LogUtility.Warning("[SaveService] SaveComponent '{0}' is not activated (block key unresolved), skipped.", component.name);
                    continue;
                }

                if (!seenKeys.Add(blockKey))
                {
                    LogUtility.Warning("[SaveService] Duplicate component block key '{0}' on '{1}', skipped.", blockKey, component.name);
                    continue;
                }

                var writer = new SaveKeyValueWriter(256);
                component.Capture(ref writer);
                entries.Add(new SaveBlockEntry(blockKey, 1, ESaveBackend.KeyValue, writer.ToArray()));
            }

            return entries;
        }

        #endregion

        #region 槽位元数据 [METADATA]

        /// <summary>
        /// 将槽位元数据异步写入存档文件（保留块 <c>__meta</c>，JSON 后端）。
        /// <para>失败抛出 <see cref="GameException"/>；处理器未就绪时静默降级为空任务。</para>
        /// </summary>
        /// <param name="metadata">槽位元数据。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static UniTask SaveMetadataAsync(SaveMetadata metadata, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler is null
                ? UniTask.CompletedTask
                : s_Handler.SaveBlockAsync(metadata, fileName, SaveServiceHandler.MetaBlockKey, folderName, ESaveBackend.Json, 1, cancellationToken);

        /// <summary>
        /// 从存档文件异步读取槽位元数据（保留块 <c>__meta</c>）。
        /// <para>元数据缺失返回 <see cref="SaveError.FileNotFound"/>（既有存档可无元数据块）；处理器未就绪时降级为 <see cref="SaveError.HandlerNotReady"/>。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>元数据加载结果。</returns>
        public static UniTask<SaveResult<SaveMetadata>> TryLoadMetadataAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) =>
            s_Handler?.TryLoadBlockAsync<SaveMetadata>(fileName, SaveServiceHandler.MetaBlockKey, folderName, cancellationToken)
                ?? UniTask.FromResult(SaveResult<SaveMetadata>.Failure(SaveError.HandlerNotReady));

        /// <summary>
        /// 将槽位元数据写入存档文件（在调用线程执行，阻塞直至完成）。
        /// </summary>
        /// <param name="metadata">槽位元数据。</param>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void SaveMetadata(SaveMetadata metadata, string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME)
        {
            if (s_Handler is null)
            {
                return;
            }

            s_Handler.SaveBlock(metadata, fileName, SaveServiceHandler.MetaBlockKey, folderName, ESaveBackend.Json, 1);
        }

        /// <summary>
        /// 从存档文件读取槽位元数据（在调用线程执行，阻塞直至完成）。
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>元数据加载结果。</returns>
        public static SaveResult<SaveMetadata> TryLoadMetadata(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.TryLoadBlock<SaveMetadata>(fileName, SaveServiceHandler.MetaBlockKey, folderName)
                ?? SaveResult<SaveMetadata>.Failure(SaveError.HandlerNotReady);

        #endregion

        #region 存档删除 [DELETE]

        /// <summary>
        /// 从磁盘中删除单个存档（含全部数据块）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        public static void DeleteSave(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DeleteSave(fileName, folderName);

        /// <summary>
        /// 删除整个存档文件夹。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        public static void DeleteSaveFolder(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DeleteSaveFolder(folderName);

        /// <summary>
        /// 删除存档数据根目录及其下所有存档。
        /// </summary>
        public static void DeleteAllSaveFiles() =>
            s_Handler?.DeleteAllSaveFiles();

        #endregion

        #region 存档查询 [QUERY]

        /// <summary>
        /// 是否存在存档文件。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public static bool FileExists(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.FileExists(fileName, folderName) ?? false;

        /// <summary>
        /// 枚举指定文件夹内的全部存档槽位（按最后写入时间倒序，最近优先）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>存档元数据数组；处理器未就绪时降级为空数组。</returns>
        public static SaveFileInfo[] GetSaveFiles(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.GetSaveFiles(folderName) ?? Array.Empty<SaveFileInfo>();

        #endregion

        #region 路径管理 [PATH]

        /// <summary>
        /// 获取文件夹的完整保存路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>保存路径；处理器未就绪时降级返回 <c>null</c>。</returns>
        public static string DetermineSavePath(string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME) =>
            s_Handler?.DetermineSavePath(folderName);

        #endregion

        #region 块描述解析 [BLOCK DESCRIPTOR]

        /// <summary>
        /// 解析数据类型的序列化后端（<see cref="SaveDataAttribute"/> 显式声明优先，未声明取 <see cref="SaveServiceSettings.DefaultBackend"/>）。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <returns>序列化后端标识。</returns>
        private static ESaveBackend ResolveBackend<T>()
        {
            if (SaveBlockDescriptor<T>.HasAttribute)
            {
                return SaveBlockDescriptor<T>.Backend;
            }

            return SaveServiceSettings.DefaultBackend;
        }

        /// <summary>
        /// 解析数据类型的模式版本（<see cref="SaveDataAttribute"/> 声明值；未声明为 1）。
        /// </summary>
        /// <typeparam name="T">存档数据类型。</typeparam>
        /// <returns>数据块模式版本。</returns>
        private static int ResolveDataVersion<T>()
        {
            return SaveBlockDescriptor<T>.Version;
        }

        #endregion
    }
}
