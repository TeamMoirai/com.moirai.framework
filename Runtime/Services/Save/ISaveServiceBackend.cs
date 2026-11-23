using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档门面能力契约（internal）：门面需要后端提供的内部能力面——原始块读写、存储后端选择、写时查询、截图写入。
    /// </summary>
    /// <remarks>
    /// 替代家族基座强转：门面经 <c>is ISaveServiceBackend</c> 能力协商取用，不再依赖 <see cref="SaveServiceHandler"/> 谱系。 <br />
    /// 实现约束：签名携带 internal 类型（<see cref="SavePaths"/>/<see cref="SaveBlockEntry"/>），实现类须**显式接口实现**转发既有 internal 成员（隐式实现会因参数可见性触发 CS0051 并外扩公开面）。 <br />
    /// 内建路径：派生 <see cref="SaveServiceHandler"/>（自动获得全部能力）；框架友元程序集（IVT 放行面）可直接实现本契约接入自定义后端。
    /// </remarks>
    internal interface ISaveServiceBackend
    {
        /// <summary>存储后端（未配置时回退共享文件后端）。</summary>
        SaveStorageBackend StorageBackend { get; }

        /// <summary>组件/具名块的原始条目 upsert（单趟合并写回，持文件级串行门）。</summary>
        UniTask UpsertRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, CancellationToken cancellationToken);

        /// <summary>读取存档文件内全部块载荷（键 → 字节；IO 与容器解析在工作线程执行）。</summary>
        UniTask<Dictionary<string, byte[]>> ReadRawBlocksAsync(SavePaths paths, CancellationToken cancellationToken);

        /// <summary>单趟合并写回（读档 → 迁移 → 双通道删除 → upsert → 写回；持文件级串行门）。</summary>
        UniTask MergeRawBlocksAsync(SavePaths paths, List<SaveBlockEntry> additions, List<string> removals, Func<Dictionary<string, byte[]>, List<string>> removalResolver, CancellationToken cancellationToken);

        /// <summary>查询存档文件最后写入时间（UTC；档缺失返回 false）。</summary>
        bool TryGetSaveWriteTimeUtc(SavePaths paths, out DateTime writeTimeUtc);

        /// <summary>写入截图 sidecar（异步 IO；须在任何线程池 await 之前读取场景名）。</summary>
        UniTask WriteScreenshotAsync(SavePaths paths, byte[] pngBytes, CancellationToken cancellationToken);

        /// <summary>同步写入数据块（扩展重载：带 backend/dataVersion 的完整管线，门面同步 API 专用）。</summary>
        void SaveBlock<T>(T data, string fileName, string key, string folderName, ushort backend, int dataVersion);
    }
}
