using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 具名数据块的批量写入：一个存档文件读一次、逐块替换、合并写回一次。
    /// </summary>
    /// <remarks>
    /// 框架公开的 <c>SaveService.SaveBlockAsync</c> 是逐块读-改-写，每块都要重读整个容器并原子重写一遍；
    /// 单趟合并的 <c>SaveServiceHandler.UpsertRawBlocksAsync</c> 是 internal，只挂在组件块与实体块路径上。
    /// 串行门、写入自愈迁移、块保存与槽位变化事件都走框架原实现。
    /// 与逐块公开 API 的差异：不触发 <c>SaveServiceSettings.CaptureScreenshotOnSave</c> 的每块截图联动（该设置当前为关，且保留块本就跳过）。
    /// </remarks>
    public sealed class SaveBlockBatch
    {
        private readonly List<PendingBlock> _pending = new List<PendingBlock>();

        /// <summary>追加一个待写入的块，编码推迟到 <see cref="CommitAsync"/> 的工作线程里执行。</summary>
        /// <param name="key">数据块键，<c>__</c> 前缀只有 <see cref="SaveService.MAIN_BLOCK_KEY"/> 合法。</param>
        /// <param name="data">块数据对象，为 null 时按框架规则抛 <see cref="ArgumentNullException"/>。</param>
        public SaveBlockBatch Add<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Save block key is null or empty.", nameof(key));
            if (data is null) throw new ArgumentNullException(nameof(data));

            ushort backend = SaveBlockDescriptor<T>.HasAttribute
                ? SaveBlockDescriptor<T>.Backend
                : SaveServiceSettings.DefaultBackend;

            _pending.Add(new PendingBlock(key, SaveBlockDescriptor<T>.Version, backend,
                                          () => SaveSerializerRegistry.GetRequired(backend).Serialize(data)));

            return this;
        }

        /// <summary>把已追加的块合并写进目标存档文件。</summary>
        /// <remarks>处理器未就绪时抛 <see cref="GameException"/>（与写路径一致，不静默丢档）。</remarks>
        public async UniTask CommitAsync(string fileName, string folderName, CancellationToken cancellationToken = default)
        {
            if (_pending.Count == 0) return;

            SaveServiceHandler handler = SaveService.Internal_PeekHandler();
            if (handler == null) throw new GameException("SaveBlockBatch: SaveService handler is not ready.");

            List<SaveBlockEntry> entries = await UniTask.RunOnThreadPool(BuildEntries, cancellationToken: cancellationToken);
            await handler.UpsertRawBlocksAsync(SaveServiceHandler.ResolveSavePaths(fileName, folderName), entries, cancellationToken);

            _pending.Clear();
        }

        private List<SaveBlockEntry> BuildEntries()
        {
            List<SaveBlockEntry> entries = new List<SaveBlockEntry>(_pending.Count);

            foreach (PendingBlock block in _pending)
            {
                entries.Add(new SaveBlockEntry(block.Key, block.DataVersion, block.Backend, block.Encode()));
            }

            return entries;
        }

        /// <summary>已登记但尚未编码的块</summary>
        private sealed class PendingBlock
        {
            public readonly string Key;
            public readonly int DataVersion;
            public readonly ushort Backend;
            public readonly Func<byte[]> Encode;

            public PendingBlock(string key, int dataVersion, ushort backend, Func<byte[]> encode)
            {
                Key = key;
                DataVersion = dataVersion;
                Backend = backend;
                Encode = encode;
            }
        }
    }
}
