using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档处理器抽象基类：承载格式与编排管线（路径解析与参数校验、多块容器组装与解析、版本化文件头读写与 CRC、载荷变换钩子、按路径串行门、槽位与块枚举），持久化 IO 委托存储层。
    /// </summary>
    /// <remarks>
    /// 序列化后端（<see cref="ISaveSerializer"/>）、存储后端（<see cref="ISaveStorage"/>）与存储管线三轴正交可插拔；文件 IO 在工作线程执行， <br />
    /// 大存档不阻塞主线程（<see cref="CancellationToken"/> 协作式取消贯穿读写）。 <br />
    /// 错误语义：写入失败 fail-fast 抛 <see cref="MoiraiException"/>（不容忍半档状态）； <br />
    /// 读取失败返回 <c>default</c> 并记错误日志（错误判别用 Try* 族的 <see cref="SaveResult{T}"/>）；删除幂等——目标不存在视为成功。 <br />
    /// 实例由 <see cref="SaveServiceSettings"/> 序列化持有，经 <see cref="SaveService"/> 静态外观访问。
    /// </remarks>
    public abstract partial class SaveServiceHandler : FrameworkHandler, ISaveService
    {
        /// <summary>存档数据根目录名（persistentDataPath 下）。</summary>
        public const string DATA_FOLDER_NAME = "Data";

        /// <summary>默认存档文件夹名。</summary>
        public const string DEFAULT_FOLDER_NAME = "Save";

        /// <summary>便捷单对象 API（Save/Load/TryLoad）映射的保留数据块。</summary>
        public const string MAIN_BLOCK_KEY = "__main__";

        /// <summary>保留块键：槽位元数据块（JSON 后端）。</summary>
        internal const string META_BLOCK_KEY = "__meta";

        /// <summary>保留块键前缀（用户块键禁止使用，避免与框架保留块冲突）。</summary>
        internal const string RESERVED_BLOCK_KEY_PREFIX = "__";

        /// <summary>块键最大字符数。</summary>
        internal const int MAX_BLOCK_KEY_LENGTH = 64;

        private static readonly char[] s_PathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        private static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly HashSet<string> s_ReservedDeviceNames = BuildReservedDeviceNames();

        /// <summary>存档根路径缓存（persistentDataPath 为 Unity API，仅在主线程首次解析）。</summary>
        [NonSerialized] private static string s_BasePath;

        /// <summary>存档根路径覆盖点（仅供测试注入；非 null 时优先于 persistentDataPath）。</summary>
        [NonSerialized] internal static string s_OverrideBasePath;

#if UNITY_EDITOR
        /// <summary>
        /// 重置静态状态：兼容关闭 Domain Reload 的编辑器工作流（进入 Play 时清空上一会话残留）。
        /// </summary>
        /// <remarks>
        /// 只清两份路径缓存：根路径由主线程首次解析重建，测试覆盖点不得跨会话泄漏（上一场测试的注入会污染下一场的落盘位置）。 <br />
        /// s_PathSeparators / s_InvalidFileNameChars / s_ReservedDeviceNames 为不可变只读集，无需归位。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_BasePath = null;
            s_OverrideBasePath = null;
        }
#endif

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
        protected override void OnInit()
        {
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
