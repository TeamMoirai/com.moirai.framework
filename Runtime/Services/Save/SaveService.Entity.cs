using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务外观——动态实体持久化分部。
    /// <para>动态实体（运行期经预制体生成的物体）按「生成表 + 每实体差分块」持久化：生成经 <see cref="InstantiatePersistent"/>/<see cref="InstantiatePersistentAsync"/>
    /// 登记，销毁经 <see cref="DestroyPersistent"/> 标记；<see cref="SaveEntitiesAsync"/> 写入实体表与差分块，
    /// <see cref="RestoreEntitiesAsync"/> 按档案状态整体重建实体（DestroyUnwanted → SpawnMissing → RestoreAll）。</para>
    /// <para>预制体须在 <see cref="SaveServiceSettings.PrefabRegistry"/> 登记（稳定键 → ResourceService 定位串）；
    /// 实体数据块与预制体模板基准差分后仅写变动字段（恢复 = 实例化模板默认值 + 应用差分）。
    /// 实体块（<c>entity:</c> 前缀）由本分部独占管理——组件存取 API（<c>SaveComponentsAsync</c>/<c>LoadComponentsAsync</c>）跳过。</para>
    /// </summary>
    public partial class SaveService
    {
        #region 实体生成与销毁 [ENTITY SPAWN / DESTROY]

        /// <summary>
        /// 生成可持久化实体（同步加载模板；主线程调用）。
        /// <para>未激活临时父技巧：先注入实体稳定 ID 与块键，就位（父级/变换）后激活——Awake 即见最终状态。
        /// 生成即登记会话生成表（<see cref="SaveEntitiesAsync"/> 据此持久化）。</para>
        /// <para>预制体未登记/加载失败（均已记录错误日志）返回 <c>null</c>；本方法不依赖存档处理器（注册表与资源服务可用即可生成）。</para>
        /// </summary>
        /// <param name="prefabKey">预制体注册键（<see cref="SavePrefabRegistry"/>）。</param>
        /// <param name="position">世界坐标。</param>
        /// <param name="rotation">世界旋转。</param>
        /// <param name="parent">父级（<c>null</c> = 场景根；父级无 <see cref="SaveObjectIdentity"/> 时父子关系不持久化）。</param>
        /// <returns>实体实例；失败返回 <c>null</c>。</returns>
        public static GameObject InstantiatePersistent(string prefabKey, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (SaveEntityPersistence.s_TemplateLoaderSync == null)
            {
                LogUtility.Error("[SaveService] Entity template loader is not wired — InstantiatePersistent unavailable.");
                return null;
            }

            GameObject template = SaveEntityPersistence.s_TemplateLoaderSync(prefabKey);
            if (template == null)
            {
                return null;
            }

            return SaveEntityPersistence.SpawnCore(template, prefabKey, null, null, position, rotation, parent);
        }

        /// <summary>
        /// 生成可持久化实体（异步加载模板；主线程续体）。
        /// <para>语义同 <see cref="InstantiatePersistent"/>；取消时返回 <c>null</c>（已加载模板随之取消，无半成品实体）。</para>
        /// </summary>
        /// <param name="prefabKey">预制体注册键（<see cref="SavePrefabRegistry"/>）。</param>
        /// <param name="position">世界坐标。</param>
        /// <param name="rotation">世界旋转。</param>
        /// <param name="parent">父级（<c>null</c> = 场景根）。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>实体实例；失败/取消返回 <c>null</c>。</returns>
        public static async UniTask<GameObject> InstantiatePersistentAsync(string prefabKey, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (SaveEntityPersistence.s_TemplateLoaderAsync == null)
            {
                LogUtility.Error("[SaveService] Entity template loader is not wired — InstantiatePersistentAsync unavailable.");
                return null;
            }

            GameObject template = await SaveEntityPersistence.s_TemplateLoaderAsync(prefabKey, cancellationToken);
            if (template == null)
            {
                return null;
            }

            return SaveEntityPersistence.SpawnCore(template, prefabKey, null, null, position, rotation, parent);
        }

        /// <summary>
        /// 销毁可持久化对象并登记销毁语义。
        /// <para>动态实体（生成表在册）：移出生成表——其数据块在下次保存时按陈旧清理。
        /// 场景预置对象（带 <see cref="SaveObjectIdentity"/>）：记入销毁表——恢复时 DestroyUnwanted 阶段销毁。
        /// 无身份组件的普通物体：仅销毁（等价 <see cref="Object.Destroy(UnityEngine.Object)"/>）。</para>
        /// </summary>
        /// <param name="target">目标物体（<c>null</c> 静默忽略）。</param>
        public static void DestroyPersistent(GameObject target)
        {
            SaveEntityPersistence.DestroyCore(target);
        }

        #endregion

        #region 实体存取 [ENTITY SAVE / RESTORE]

        /// <summary>
        /// 将实体表与全部活跃实体的差分块异步写入存档文件（主线程捕获，IO 在工作线程）。
        /// <para>流程：预热模板基准（异步加载，避免捕获期卡顿）→ 逐实体差分捕获 → 增量判定 → 单趟合并写回。
        /// 失败抛出 <see cref="GameException"/>；处理器未就绪时抛 <see cref="GameException"/>（不静默丢档）。</para>
        /// <para>CarryForward 语义：保存仅 upsert 活跃实体，未访问场景与生成失败实体的块原样滞留。</para>
        /// <para>增量语义：会话级脏跟踪 + 档写入时间守卫——实体表、差分载荷与档均未变化时零 IO 跳过（无事件）；
        /// 有变化时经 <see cref="SaveServiceHandler.MergeRawBlocksAsync"/> 单趟合并（读档 → 迁移 → 删陈旧 → upsert → 写回），
        /// 仅变化块触发 <c>BlockSaved</c> 事件；基准失效（首次保存/恢复后/档被外部改写）自动走全量合并（孤儿清理读档判定）。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>写入完成的异步任务。</returns>
        public static async UniTask SaveEntitiesAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            SaveServiceHandler handler = RequireHandler();
            await SaveEntityPersistence.WarmBaselinesAsync(cancellationToken);
            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries(fileName, folderName);
            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);

            if (SaveEntityPersistence.TrySkipEntitySave(paths, entries, handler, out List<SaveBlockEntry> dirtyEntries, out List<string> certainRemovals))
            {
                return;
            }

            // 增量模式：确定删除集（会话判定，无需读档）；全量模式（基准失效）：全部 upsert + 孤儿清理读档判定
            bool incremental = dirtyEntries != null;
            List<SaveBlockEntry> additions = incremental ? dirtyEntries : entries;
            List<string> removals = incremental ? certainRemovals : null;
            System.Func<System.Collections.Generic.Dictionary<string, byte[]>, List<string>> removalResolver = incremental ? null : SaveEntityPersistence.StaleKeyResolver;

            await handler.MergeRawBlocksAsync(paths, additions, removals, removalResolver, cancellationToken);

            // merge 成功后回读档写入时间提交增量基准（下次保存的脏判定基准）
            if (handler.TryGetSaveWriteTimeUtc(paths, out System.DateTime writeTimeUtc))
            {
                SaveEntityPersistence.CommitSavedState(paths, entries, writeTimeUtc);
            }
        }

        /// <summary>
        /// 从存档文件异步重建全部动态实体（模板加载在工作线程等待，场景操作在主线程）。
        /// <para>管线：DestroyUnwanted（会话实体整体替换 + 销毁表预置对象）→ SpawnMissing（原 ID 恢复 + 场景落位）
        /// → 父子接线（第二轮）→ RestoreAll（差分块未激活写回，Awake 即见恢复后状态）→ 激活并逐只触发 <see cref="EntityRestored"/>。
        /// 生成失败的实体保留档案记录（原块滞留，不因本次保存丢失）；处理器未就绪时静默降级为空任务。</para>
        /// <para>场景预置对象的字段恢复由 <c>LoadComponentsAsync</c> 承担——完整世界恢复 = <see cref="RestoreEntitiesAsync"/> + <c>LoadComponentsAsync</c>。</para>
        /// </summary>
        /// <param name="fileName">文件名（自动追加配置的扩展名）。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <param name="cancellationToken">取消令牌（协作式）。</param>
        /// <returns>恢复完成的异步任务。</returns>
        public static async UniTask RestoreEntitiesAsync(string fileName, string folderName = SaveServiceHandler.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default)
        {
            if (s_Handler is null)
            {
                return;
            }

            SaveServiceHandler.SavePaths paths = SaveServiceHandler.ResolveSavePaths(fileName, folderName);
            Dictionary<string, byte[]> blocks = await s_Handler.ReadRawBlocksAsync(paths, cancellationToken);

            // 读档续体经 RunOnThreadPool(configureAwait:false) 完成——可能停留在线程池；
            // 恢复管线全程 Unity API（生成/销毁/变换写回/事件内联派发），须先切回主线程
            if (!MainThreadDispatcher.IsMainThread)
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }

            await SaveEntityPersistence.RestoreFromBlocksAsync(blocks, fileName, folderName, cancellationToken);
        }

        #endregion
    }
}
