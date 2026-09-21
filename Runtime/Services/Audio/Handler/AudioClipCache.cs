using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioClip 生产级缓存：Lease 保留 + 引用计数 + LRU/TTL/Pin + lowMemory + 容量驱逐。
    /// <para>路径播放的唯一资源真相源：同一地址全服务共享一份租约，声部按引用取用，用完按策略决定留池或释放。</para>
    /// <para>不变量：</para>
    /// <para>1. 条目仅在 <c>RefCount == 0 &amp;&amp; !Loading &amp;&amp; 无等待者</c> 时可被驱逐；</para>
    /// <para>2. Pin 条目不入 LRU，只被 <see cref="Unload"/>/<see cref="ClearCache"/> 显式摘除；</para>
    /// <para>3. 条目数不超过 <see cref="Capacity"/>——满载且无可驱逐对象时新地址直接判负，不无界增长；</para>
    /// <para>4. 迟到的加载续体按 <c>Version</c> 作废，租约归还资源系统而不写入已复用的条目。</para>
    /// </summary>
    internal sealed class AudioClipCache
    {
        public const int DefaultCapacity = 128;
        public const float DefaultTtl = 30f;

        private readonly Dictionary<string, AudioClipCacheEntry> _entries = new Dictionary<string, AudioClipCacheEntry>(64);

        /// <summary>池可见视图（已加载且留池的条目）——兼容 <c>AssetHandlePool</c> 只读 API。</summary>
        public readonly Dictionary<string, object> PoolView = new Dictionary<string, object>(32);

        private AudioClipCacheEntry _lruHead;
        private AudioClipCacheEntry _lruTail;
        private AudioClipCacheEntry _allHead;
        private AudioClipCacheEntry _allTail;
        private IAudioClipLeaseSource _source;
        private int _capacity = DefaultCapacity;
        private float _ttl = DefaultTtl;
        private AudioCachePolicy _defaultPolicy = AudioCachePolicy.Ttl;
        private bool _lowMemoryRegistered;
        private bool _disposed;

        /// <summary>当前缓存条目数。</summary>
        public int Count => _entries.Count;

        /// <summary>All 链头：诊断/调试面板遍历入口。</summary>
        public AudioClipCacheEntry FirstEntry => _allHead;

        /// <summary>LRU 链头：最久未用的可驱逐条目（驱逐与 TTL 都从头开始）。</summary>
        public AudioClipCacheEntry FirstLruEntry => _lruHead;

        /// <summary>容量上限。</summary>
        public int Capacity => _capacity;

        /// <summary>TTL 秒数（0 表示不按时间驱逐）。</summary>
        public float Ttl => _ttl;

        /// <summary>默认策略。</summary>
        public AudioCachePolicy DefaultPolicy => _defaultPolicy;

        /// <summary>进行中的加载数（诊断用，按需遍历）。</summary>
        public int LoadingCount
        {
            get
            {
                int count = 0;
                for (var e = _allHead; e != null; e = e.AllNext)
                {
                    if (e.Loading) count++;
                }

                return count;
            }
        }

        /// <summary>常驻（Pin）条目数（诊断用，按需遍历）。</summary>
        public int PinnedCount
        {
            get
            {
                int count = 0;
                for (var e = _allHead; e != null; e = e.AllNext)
                {
                    if (e.Pinned) count++;
                }

                return count;
            }
        }

        /// <summary>
        /// 应用租约来源与容量/TTL/默认策略，并注册 lowMemory 回调。<see cref="UnityAudioHandler"/> 每次 Initialize 调用。
        /// </summary>
        public void Configure(IAudioClipLeaseSource source, int capacity, float ttl, AudioCachePolicy defaultPolicy)
        {
            _source = source;
            _capacity = Mathf.Max(1, capacity);
            _ttl = Mathf.Max(0f, ttl);
            _defaultPolicy = NormalizeDefaultPolicy(defaultPolicy);
            // 关停后同一处理器实例可被重新初始化（Restart / 测试复用），否则缓存会永久失活
            _disposed = false;
            RegisterLowMemory();
        }

        /// <summary>
        /// 释放全部租约并注销回调。关停路径无视引用计数强制回收（声部应已停播）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnregisterLowMemory();
            while (_allHead != null)
            {
                RemoveEntry(_allHead, ignoreRefCount: true);
            }

            PoolView.Clear();
            _source = null;
        }

        /// <summary>把 <see cref="AudioCachePolicy.Default"/> 解析为配置的默认策略。</summary>
        public AudioCachePolicy ResolvePolicy(AudioCachePolicy policy)
        {
            return policy switch
            {
                AudioCachePolicy.None or AudioCachePolicy.Ttl or AudioCachePolicy.Pin => policy,
                _ => _defaultPolicy,
            };
        }

        private static AudioCachePolicy NormalizeDefaultPolicy(AudioCachePolicy policy)
        {
            return policy switch
            {
                AudioCachePolicy.None or AudioCachePolicy.Ttl or AudioCachePolicy.Pin => policy,
                _ => AudioCachePolicy.Ttl,
            };
        }

        /// <summary>路径是否已在缓存中加载完成。</summary>
        public bool TryGetLoaded(string address, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(address) || !_entries.TryGetValue(address, out var e)) return false;
            if (!e.IsLoaded) return false;
            entry = e;
            return true;
        }

        /// <summary>获取条目（不引用计数，仅查询/诊断用）。</summary>
        public bool TryGetEntry(string address, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(address)) return false;
            return _entries.TryGetValue(address, out entry);
        }

        /// <summary>
        /// 声部请求 clip：命中则回调就绪；未命中则挂等待者并按需启动单飞加载。
        /// <para>同地址并发请求共用一次加载，等待者按请求顺序起播。</para>
        /// </summary>
        /// <param name="generation">声部加载世代；完成回调与之不符时落空（声部已换曲/已停播）。</param>
        /// <returns>已就绪或加载已受理返回 true；地址无效、满载或后端不可用返回 false。</returns>
        public bool RequestClip(string address, bool async, AudioCachePolicy policy, AudioAgent agent, int generation)
        {
            if (agent == null || string.IsNullOrEmpty(address) || _source == null || _disposed)
            {
                return false;
            }

            var resolved = ResolvePolicy(policy);
            var entry = GetOrCreate(address, resolved);
            if (entry == null)
            {
                return false;
            }

            UpgradePolicy(entry, resolved);
            Touch(entry);

            if (entry.IsLoaded)
            {
                return agent.OnClipReady(entry, generation);
            }

            var request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Agent = agent;
            request.Generation = generation;
            entry.AddPending(request);
            agent.SetLoadRequest(request);

            if (entry.Loading)
            {
                // 同地址已有在途加载：只排队等待，绝不二次向后端取租约
                return true;
            }

            return BeginLoad(entry, async);
        }

        /// <summary>
        /// 预加载（同步）：<paramref name="policy"/> 为 Default 时取配置默认策略。
        /// </summary>
        /// <returns>已加载完成返回 true；加载中、满载或失败返回 false。</returns>
        public bool Preload(string address, AudioCachePolicy policy = AudioCachePolicy.Pin)
        {
            if (!TryPreparePreload(address, policy, out var entry))
            {
                return false;
            }

            if (entry.IsLoaded) return true;
            if (entry.Loading) return false;
            return BeginLoad(entry, async: false);
        }

        /// <summary>
        /// 预加载（异步）：完成时回调 <paramref name="completed"/>；同地址多次调用会共享一次加载。
        /// </summary>
        public void PreloadAsync(string address, AudioCachePolicy policy, Action<bool> completed = null)
        {
            if (!TryPreparePreload(address, policy, out var entry))
            {
                completed?.Invoke(false);
                return;
            }

            if (entry.IsLoaded)
            {
                completed?.Invoke(true);
                return;
            }

            var request = MemoryPool.Acquire<AudioLoadRequest>();
            request.Completed = completed;
            entry.AddPending(request);

            if (!entry.Loading)
            {
                BeginLoad(entry, async: true);
            }
        }

        /// <summary>
        /// 声部停播/换曲时注销挂起请求：条目上再无其它等待者且无引用时立即回收，避免半路加载占位。
        /// </summary>
        internal void CancelLoadRequest(AudioLoadRequest request)
        {
            var entry = request.Entry;
            if (entry == null)
            {
                MemoryPool.Release(request);
                return;
            }

            entry.RemovePending(request);
            MemoryPool.Release(request);
            if (entry.PendingHead == null && entry.RefCount == 0 && !entry.Pinned && !entry.Loading)
            {
                RemoveEntry(entry);
            }
        }

        /// <summary>
        /// 卸载地址缓存。<paramref name="force"/> 只放宽「Pin 与挂起等待者」两道门槛；
        /// 仍有声部在引用（播放/淡出中）时一律拒绝——强制释放会让在播的 <see cref="AudioClip"/> 变成已卸载资源。
        /// </summary>
        public bool Unload(string address, bool force = false)
        {
            if (string.IsNullOrEmpty(address) || !_entries.TryGetValue(address, out var entry)) return false;
            if (entry.RefCount != 0 || entry.Loading) return false;
            if (!force && (entry.Pinned || entry.PendingHead != null)) return false;

            RemoveEntry(entry);
            return true;
        }

        /// <summary>
        /// 清空缓存。<paramref name="force"/> 连 Pin 一并清；在播引用始终不清。
        /// </summary>
        public void ClearCache(bool force = false)
        {
            var entry = _allHead;
            while (entry != null)
            {
                var next = entry.AllNext;
                if (entry.RefCount == 0 && !entry.Loading && (force || (!entry.Pinned && entry.PendingHead == null)))
                {
                    RemoveEntry(entry);
                }

                entry = next;
            }
        }

        /// <summary>Tick：TTL 驱逐（只扫 LRU 头，无引用且过期的连续淘汰）。</summary>
        public void Tick()
        {
            if (_disposed || _ttl <= 0f) return;

            float now = Time.realtimeSinceStartup;
            while (_lruHead != null && now - _lruHead.LastUseTime >= _ttl)
            {
                RemoveEntry(_lruHead);
            }
        }

        /// <summary>Retain：正在使用的 clip 不可被 LRU/TTL 驱逐。</summary>
        public void Retain(AudioClipCacheEntry entry)
        {
            if (entry == null) return;
            entry.RefCount++;
            RemoveFromLru(entry);
        }

        /// <summary>Release：归零后按策略回 LRU 或直接释放。</summary>
        public void Release(AudioClipCacheEntry entry)
        {
            if (entry == null) return;
            if (entry.RefCount <= 0)
            {
                LogUtility.Error("[AudioClipCache] Release() on an unretained entry '{0}'; ref count would go negative.", entry.Address);
                return;
            }

            if (--entry.RefCount > 0 || entry.Loading) return;

            if (!entry.CacheAfterUse)
            {
                RemoveEntry(entry);
                return;
            }

            entry.LastUseTime = Time.realtimeSinceStartup;
            if (!entry.Pinned)
            {
                AddToLruTail(entry);
            }

            SyncPoolView(entry);
        }

        /// <summary>低内存：清非 Pin、无引用缓存。</summary>
        public void OnLowMemory()
        {
            if (_disposed) return;
            ClearCache(force: false);
        }

        private static bool CanEvict(AudioClipCacheEntry entry)
            => entry.RefCount == 0 && !entry.Loading && entry.PendingHead == null;

        private bool TryPreparePreload(string address, AudioCachePolicy policy, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(address) || _source == null || _disposed) return false;

            var resolved = ResolvePolicy(policy);
            entry = GetOrCreate(address, resolved);
            if (entry == null) return false;

            UpgradePolicy(entry, resolved);
            Touch(entry);
            SyncPoolView(entry);
            return true;
        }

        private AudioClipCacheEntry GetOrCreate(string address, AudioCachePolicy policy)
        {
            if (_entries.TryGetValue(address, out var existing)) return existing;

            // 满载：先淘汰最久未用的无引用条目；全 Pin/全占用则拒绝新地址（宁可漏播，不可无界驻留）
            if (_entries.Count >= _capacity && _lruHead == null) return null;
            if (_entries.Count >= _capacity) RemoveEntry(_lruHead);

            var entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Initialize(this, address, address.GetHashCode() & 0x7fffffff, policy);
            _entries[address] = entry;
            AddToAllList(entry);
            return entry;
        }

        /// <summary>策略只升不降：Pin 永不被降级，None 不会把已缓存的条目改成不缓存。</summary>
        private void UpgradePolicy(AudioClipCacheEntry entry, AudioCachePolicy policy)
        {
            if (policy == AudioCachePolicy.None) return;
            if (entry.CachePolicy == AudioCachePolicy.Pin) return;

            if (policy == AudioCachePolicy.Pin)
            {
                entry.CachePolicy = AudioCachePolicy.Pin;
                RemoveFromLru(entry);
                SyncPoolView(entry);
                return;
            }

            if (entry.CachePolicy == AudioCachePolicy.None)
            {
                entry.CachePolicy = AudioCachePolicy.Ttl;
            }
        }

        private bool BeginLoad(AudioClipCacheEntry entry, bool async)
        {
            RemoveFromLru(entry);
            entry.Loading = true;
            ulong version = entry.Version;

            if (async)
            {
                entry.Cancellation ??= new CancellationTokenSource();
                // 回调携带 entry 引用：迟到的完成由 version 与 _entries 身份校验拦下
                _source.AcquireAsync(entry.Address, entry.Cancellation.Token,
                    lease => OnLoadCompleted(entry, version, lease));
                return true;
            }

            AudioClipLease lease = default;
            try
            {
                if (!_source.TryAcquire(entry.Address, out lease) || !lease.IsValid)
                {
                    lease.Release();
                    OnLoadCompleted(entry, version, default);
                    return false;
                }
            }
            catch (Exception e)
            {
                // 不外抛：失败按返回 false + 等待者回调通知，抛出会把调用方声部永久卡在 Loading
                lease.Release();
                LogUtility.Error("[AudioClipCache] Sync lease of '{0}' failed: {1}", entry.Address, e.Message);
                OnLoadCompleted(entry, version, default);
                return false;
            }

            return OnLoadCompleted(entry, version, lease);
        }

        private bool OnLoadCompleted(AudioClipCacheEntry entry, ulong version, AudioClipLease lease)
        {
            // 世代/身份校验：条目已被驱逐或已复用为另一地址时，本次结果作废并归还租约。
            // Address 判空必须前置——关停路径上条目已 Clear()，用它查字典会抛 ArgumentNullException。
            if (entry.Address == null ||
                entry.Version != version ||
                !_entries.TryGetValue(entry.Address, out var current) ||
                !ReferenceEquals(current, entry))
            {
                lease.Release();
                return false;
            }

            entry.Loading = false;
            bool success = lease.IsValid;
            if (!success)
            {
                lease.Release();
            }

            entry.Lease.Release();
            entry.Lease = success ? lease : default;
            entry.Clip = success ? lease.Clip : null;

            // 派发期间自持一份引用：等待者回调里的请求/卸载不能中途把本条目挤成负引用或被驱逐
            Retain(entry);
            AudioLoadRequest callbacks = CompletePending(entry, success);
            Release(entry);
            CompletePreloads(callbacks, success);
            SyncPoolView(entry);

            if (!success && CanEvict(entry))
            {
                RemoveEntry(entry);
            }

            return success;
        }

        /// <summary>
        /// 摘空挂起队列：声部等待者就地回调，仅带 <see cref="AudioLoadRequest.Completed"/> 的预载回调
        /// 串成链表返回给调用方，等派发引用释放后再执行——预载回调里可以安全地再次请求同一地址。
        /// </summary>
        private static AudioLoadRequest CompletePending(AudioClipCacheEntry entry, bool success)
        {
            var request = entry.PendingHead;
            entry.PendingHead = null;
            entry.PendingTail = null;

            AudioLoadRequest callbackHead = null;
            AudioLoadRequest callbackTail = null;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                request.Entry = null;
                request.Prev = null;
                request.Next = null;

                if (request.Agent != null)
                {
                    if (success) request.Agent.OnClipReady(entry, request.Generation);
                    else request.Agent.OnClipLoadFailed(request.Generation);

                    MemoryPool.Release(request);
                }
                else if (request.Completed != null)
                {
                    if (callbackHead == null) callbackHead = request;
                    else callbackTail.Next = request;
                    callbackTail = request;
                }
                else
                {
                    MemoryPool.Release(request);
                }

                request = next;
            }

            return callbackHead;
        }

        private static void CompletePreloads(AudioLoadRequest request, bool success)
        {
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                Action<bool> completed = request.Completed;
                MemoryPool.Release(request);
                try
                {
                    completed(success);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                request = next;
            }
        }

        private void Touch(AudioClipCacheEntry entry)
        {
            entry.LastUseTime = Time.realtimeSinceStartup;
            MoveLruToTail(entry);
        }

        /// <summary>
        /// 摘除条目：脱离全部索引与视图 → 通知等待者 → 归还条目（<c>Clear</c> 释放租约与 CTS）。
        /// </summary>
        /// <param name="ignoreRefCount">关停路径强制回收；常规驱逐必须为 false。</param>
        private void RemoveEntry(AudioClipCacheEntry entry, bool ignoreRefCount = false)
        {
            if (entry == null || entry.Address == null) return;
            if (!ignoreRefCount && entry.RefCount != 0) return;

            if (entry.Loading)
            {
                try
                {
                    entry.Cancellation?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // CTS 已随上一次归还释放，取消已无意义
                }
            }

            _entries.Remove(entry.Address);
            RemoveFromLru(entry);
            RemoveFromAllList(entry);
            PoolView.Remove(entry.Address);

            AudioLoadRequest callbacks = CompletePending(entry, false);
            MemoryPool.Release(entry);
            CompletePreloads(callbacks, false);
        }

        /// <summary>
        /// 把留池条目投影到 <c>AssetHandlePool</c> 兼容视图。值是 <see cref="AudioClipLease"/> 的装箱副本，
        /// 只能用于枚举观测——租约本体仍由缓存持有，外部无法经视图释放它。
        /// </summary>
        private void SyncPoolView(AudioClipCacheEntry entry)
        {
            if (entry?.Address == null) return;

            if (entry.IsLoaded && (entry.Pinned || entry.CacheAfterUse))
            {
                PoolView[entry.Address] = entry.Lease;
            }
            else
            {
                PoolView.Remove(entry.Address);
            }
        }

        private void AddToLruTail(AudioClipCacheEntry entry)
        {
            if (entry.InLru) return;
            entry.InLru = true;
            entry.LruPrev = _lruTail;
            entry.LruNext = null;
            if (_lruTail != null) _lruTail.LruNext = entry;
            else _lruHead = entry;
            _lruTail = entry;
        }

        private void RemoveFromLru(AudioClipCacheEntry entry)
        {
            if (!entry.InLru) return;
            var prev = entry.LruPrev;
            var next = entry.LruNext;
            if (prev != null) prev.LruNext = next;
            else _lruHead = next;
            if (next != null) next.LruPrev = prev;
            else _lruTail = prev;
            entry.LruPrev = null;
            entry.LruNext = null;
            entry.InLru = false;
        }

        private void MoveLruToTail(AudioClipCacheEntry entry)
        {
            if (!entry.InLru || ReferenceEquals(_lruTail, entry)) return;
            RemoveFromLru(entry);
            AddToLruTail(entry);
        }

        private void AddToAllList(AudioClipCacheEntry entry)
        {
            entry.AllPrev = _allTail;
            entry.AllNext = null;
            if (_allTail != null) _allTail.AllNext = entry;
            else _allHead = entry;
            _allTail = entry;
        }

        private void RemoveFromAllList(AudioClipCacheEntry entry)
        {
            var prev = entry.AllPrev;
            var next = entry.AllNext;
            if (prev != null) prev.AllNext = next;
            else _allHead = next;
            if (next != null) next.AllPrev = prev;
            else _allTail = prev;
            entry.AllPrev = null;
            entry.AllNext = null;
        }

        private void RegisterLowMemory()
        {
            if (_lowMemoryRegistered) return;
            Application.lowMemory += OnLowMemory;
            _lowMemoryRegistered = true;
        }

        private void UnregisterLowMemory()
        {
            if (!_lowMemoryRegistered) return;
            Application.lowMemory -= OnLowMemory;
            _lowMemoryRegistered = false;
        }
    }
}
