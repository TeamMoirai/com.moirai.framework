using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档处理器抽象基类：承载格式与编排管线（路径解析与参数校验、多块容器组装与解析、版本化文件头读写与 CRC、载荷变换钩子、按路径串行门、槽位与块枚举），持久化 IO 委托存储层。
    /// </summary>
    /// <remarks>
    /// 序列化后端（<see cref="ISaveSerializer"/>）、存储后端（<see cref="ISaveStorage"/>）与存储管线三轴正交可插拔；文件 IO 在工作线程执行， <br />
    /// 大存档不阻塞主线程（<see cref="CancellationToken"/> 协作式取消贯穿读写）。 <br />
    /// 错误语义：写入失败 fail-fast 抛 <see cref="GameException"/>（不容忍半档状态）； <br />
    /// 读取失败返回 <c>default</c> 并记错误日志（错误判别用 Try* 族的 <see cref="SaveResult{T}"/>）；删除幂等——目标不存在视为成功。 <br />
    /// 实例由 <see cref="SaveServiceSettings"/> 序列化持有，经 <see cref="SaveService"/> 静态外观访问。
    /// </remarks>
    [Serializable]
    [ServiceDependency(typeof(IDebuggerService))]
    public abstract partial class SaveServiceHandler : ServiceBase, ISaveService, ISaveServiceBackend
    {
        /// <summary>存档数据根目录名（persistentDataPath 下）。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        public const string DATA_FOLDER_NAME = SaveServiceDefaults.DATA_FOLDER_NAME;

        /// <summary>默认存档文件夹名。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        public const string DEFAULT_FOLDER_NAME = SaveServiceDefaults.DEFAULT_FOLDER_NAME;

        /// <summary>便捷单对象 API（Save/Load/TryLoad）映射的保留数据块。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        public const string MAIN_BLOCK_KEY = SaveServiceDefaults.MAIN_BLOCK_KEY;

        /// <summary>保留块键：槽位元数据块（JSON 后端）。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        internal const string META_BLOCK_KEY = SaveServiceDefaults.META_BLOCK_KEY;

        /// <summary>保留块键前缀（用户块键禁止使用，避免与框架保留块冲突）。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        internal const string RESERVED_BLOCK_KEY_PREFIX = SaveServiceDefaults.RESERVED_BLOCK_KEY_PREFIX;

        /// <summary>块键最大字符数。源自 <see cref="SaveServiceDefaults"/>（中立宿主）。</summary>
        internal const int MAX_BLOCK_KEY_LENGTH = SaveServiceDefaults.MAX_BLOCK_KEY_LENGTH;

        /// <summary>存档根路径覆盖点（仅供测试注入；非 null 时优先于 persistentDataPath）。转发 <see cref="SavePathResolver.s_OverrideBasePath"/>。</summary>
        internal static string s_OverrideBasePath
        {
            get => SavePathResolver.s_OverrideBasePath;
            set => SavePathResolver.s_OverrideBasePath = value;
        }

        [Tooltip("存储后端：存档 IO 的下沉目标（默认本地文件；云存档等自定义后端继承 SaveStorageBackend 接入）。置空时回退本地文件后端。")]
        [ProviderDropdown]
        [SerializeReference] private SaveStorageBackend m_StorageBackend = new FileSaveStorageBackend();

        [Tooltip("压缩提供方：容器字节在加密前压缩（空 = 不压缩）。写出档的文件头记录提供方 ID；自定义提供方须注册到 SaveCompressionRegistry 才能读回旧档。")]
        [ProviderDropdown]
        [SerializeReference] private SaveCompressionProvider m_CompressionProvider;

        [Tooltip("迁移回写：加载触发版本迁移成功后将迁移结果惰性回写存档（默认开）。关闭时迁移仅作用于当次加载的内存数据，同文件同会话不重复迁移（经会话级缓存），但存档文件保持旧版本。")]
        [SerializeField] private bool m_MigrationWriteBack = true;

        /// <summary>存储后端（未配置时回退共享文件后端）。</summary>
        /// <remarks>
        /// 严禁在核心管线惰性触达 <see cref="SaveServiceSettings"/>（<c>Resources.Load</c> 仅限主线程，工作线程触达即崩）。
        /// </remarks>
        internal SaveStorageBackend StorageBackend => m_StorageBackend ?? FileSaveStorageBackend.s_Default;

        #region 能力契约实现 [ISaveServiceBackend]

        // 显式接口实现转发既有 internal 成员（签名携带 internal 类型，隐式实现触发 CS0051 并外扩公开面）；
        // 门面经 is ISaveServiceBackend 能力协商取用，家族基座谱系随之降级为实现路径而非语义要求。

        SaveStorageBackend ISaveServiceBackend.StorageBackend => StorageBackend;

        UniTask ISaveServiceBackend.UpsertRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, CancellationToken cancellationToken)
            => UpsertRawBlocksAsync(paths, additions, cancellationToken);

        UniTask<Dictionary<string, byte[]>> ISaveServiceBackend.ReadRawBlocksAsync(SavePaths paths, CancellationToken cancellationToken)
            => ReadRawBlocksAsync(paths, cancellationToken);

        UniTask ISaveServiceBackend.MergeRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, List<string> removals, Func<Dictionary<string, byte[]>, List<string>> removalResolver, CancellationToken cancellationToken)
            => MergeRawBlocksAsync(paths, additions, removals, removalResolver, cancellationToken);

        bool ISaveServiceBackend.TryGetSaveWriteTimeUtc(SavePaths paths, out DateTime writeTimeUtc)
            => TryGetSaveWriteTimeUtc(paths, out writeTimeUtc);

        UniTask ISaveServiceBackend.WriteScreenshotAsync(SavePaths paths, byte[] pngBytes, CancellationToken cancellationToken)
            => WriteScreenshotAsync(paths, pngBytes, cancellationToken);

        void ISaveServiceBackend.SaveBlock<T>(T data, string fileName, string key, string folderName, ushort backend, int dataVersion)
            => SaveBlock(data, fileName, key, folderName, backend, dataVersion);

        #endregion 能力契约实现 [ISaveServiceBackend]

        /// <summary>压缩提供方（<c>null</c> = 不压缩；无状态纯 .NET，工作线程调用安全）。</summary>
        internal SaveCompressionProvider CompressionProvider
        {
            get => m_CompressionProvider;
            set => m_CompressionProvider = value;
        }

        /// <summary>迁移回写开关（纯数据，工作线程读取安全）。</summary>
        internal bool MigrationWriteBack
        {
            get => m_MigrationWriteBack;
            set => m_MigrationWriteBack = value;
        }

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化存档处理器。由容器在构建期调用（主线程：校验存储后端配置，并后台清扫孤儿临时文件）。
        /// </summary>
        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 关闭存档处理器。由容器在关闭期驱动（注销/世界收口）：容器编排收尾随行。
        /// </summary>
        protected override void OnShutdown()
        {
            // 槽位失效只在真实容器拆除时发生（State 门）：handler 内部复用 OnShutdown 做引导重置时不清槽
            if (State >= EServiceState.ShuttingDown)
            {
                SaveService.InvalidateHandler(this);
            }
            // 编排收尾以「装配过」为闸（就绪判据在关停体内恒假）：世界销毁兜底会对未 OnInit 的服务也驱动 OnShutdown，此时依赖可能已先被拆走
            if (WasInitialized && Context != null)
            {
                SaveService.OnHandlerShuttingDown(Context.GetRequiredService<IDebuggerService>());
            }
        }

        /// <summary>
        /// 初始化存档处理器。由容器在拓扑位驱动：容器编排（调试面板注册）以 Context 就位为闸随行。
        /// </summary>
        protected override void OnInit()
        {
            if (Context != null)
            {
                SaveService.OnHandlerInitialized(Context.GetRequiredService<IDebuggerService>());
            }

            if (m_StorageBackend == null)
            {
                LogUtility.Warning("[SaveService] Storage backend is not configured, falling back to FileSaveStorageBackend.");
            }

            // 后台先抬回上次写入中断留下的旧档，再清扫孤儿临时文件；根目录须在主线程解析（persistentDataPath 为 Unity API）
            SaveStorageBackend backend = StorageBackend;
            string rootDirectory = BuildDataRootDirectory();
            _ = UniTask.RunOnThreadPool(() =>
            {
                // 维护段持根级门，与所有存档 IO 互斥：恢复会在回退替换的两步改名中间把 journal 抬成主档，
                // 清空全部存档也可能与恢复对撞——持门写法与 TryDeleteAllSaveFiles 同一把根级门。
                GateScope scope = GateScope.EnterRoot(rootDirectory);
                scope.Wait();
                try
                {
                    backend.RecoverInterruptedWrites(rootDirectory);
                    backend.CleanupOrphanTempFiles(rootDirectory);
                }
                finally
                {
                    scope.Leave();
                }
            }, configureAwait: false);
        }

        #endregion









    }
}
