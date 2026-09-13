using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 云端存档 KV 存储抽象（远端 KV 语义；框架插拔件惯例：[Serializable] 抽象基类，
    /// 由 <see cref="CloudSaveStorageBackend"/> 以 [SerializeReference] + ProviderDropdown 持有）。
    /// <para>键规范：相对存档根目录（<c>persistentDataPath/Data/</c>）的路径，<c>/</c> 分隔（如 <c>Save/slot1.sav</c>）——
    /// 不携带本机目录结构，跨设备一致。</para>
    /// <para>错误语义：远端不可达/IO 失败/未登录一律抛异常（类型不限），由 <see cref="CloudSaveStorageBackend"/> 归一为离线降级；
    /// 缺档非错误——<see cref="ReadAsync"/> 返回 <c>null</c>、<see cref="ExistsAsync"/> 返回 <c>false</c>、<see cref="DeleteAsync"/> 幂等。</para>
    /// <para>实现须为纯 .NET 逻辑（可在任意线程调用），禁止触达 Unity 主线程 API；时间戳由远端权威时钟给出（<c>DateTimeKind.Utc</c>）。</para>
    /// </summary>
    [Serializable]
    public abstract class CloudSaveKvStore
    {
        /// <summary>
        /// 读取远端条目（缺档返回 <c>null</c>；远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>条目（含远端时间戳）；缺档为 <c>null</c>。</returns>
        public abstract UniTask<CloudKvEntry?> ReadAsync(string key, CancellationToken cancellationToken);

        /// <summary>
        /// 远端是否存在目标键（远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public abstract UniTask<bool> ExistsAsync(string key, CancellationToken cancellationToken);

        /// <summary>
        /// 写入远端条目（整值替换；远端失败抛异常；时间戳由远端权威时钟盖章）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>写入完成的异步任务。</returns>
        public abstract UniTask WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken);

        /// <summary>
        /// 删除远端条目（幂等——不存在视为成功；远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public abstract UniTask DeleteAsync(string key, CancellationToken cancellationToken);

        /// <summary>
        /// 枚举远端全部条目（远端失败抛异常；空仓库返回空数组）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>条目元信息数组。</returns>
        public abstract UniTask<CloudKvEntryInfo[]> EnumerateAsync(CancellationToken cancellationToken);
    }
}
