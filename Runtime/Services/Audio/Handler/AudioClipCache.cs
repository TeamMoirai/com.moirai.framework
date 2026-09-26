using System;
using System.Collections;
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

        /// <summary>加载失败后该地址的冷却时长（秒）。防住一个写错的事件地址被高频触发时每次重穿资源层。</summary>
        public const float FailureCooldownSeconds = 5f;

        /// <summary>失败过的地址 → 冷却截止时刻（<c>Time.realtimeSinceStartup</c> 口径）。</summary>
        private readonly Dictionary<string, float> _failedUntil = new Dictionary<string, float>(8);

        // 地址 → 条目的开址定长槽表。取代原先的 Dictionary<string, AudioClipCacheEntry>：
        // 播放/停播/驱逐全走这张表，而字典只会长大不会缩，"条目数有上限"约束的是表外的世界。
        private AudioClipCacheEntry[] _slots;
        private int[] _freeSlots;
        private int[] _buckets;
        private int _freeCount;
        private int _bucketMask;
        private int _count;

        private AudioClipCacheEntry _lruHead;
        private AudioClipCacheEntry _lruTail;
        private AudioClipCacheEntry _allHead;
        private AudioClipCacheEntry _allTail;
        private IReadOnlyDictionary<string, object> _poolView;
        private IAudioClipLeaseSource _source;
        private int _capacity = DefaultCapacity;
        private float _ttl = DefaultTtl;
        private float _failureCooldown = FailureCooldownSeconds;
        private EAudioCachePolicy _defaultPolicy = EAudioCachePolicy.Ttl;
        private bool _lowMemoryRegistered;
        private bool _disposed;

        /// <summary>当前缓存条目数。</summary>
        public int Count => _count;

        /// <summary>失败冷却中的地址数（诊断用）。</summary>
        public int FailedAddressCount => _failedUntil.Count;

        /// <summary>
        /// 留池视图（已加载且留池的条目）——兼容 <c>AssetHandlePool</c> 只读 API。
        /// <para>它是槽表的**计算视图**而不是镜像表：缓存只在租约换手处装箱一次都不做，
        /// 因为除调试面板外无人枚举它，而把它维护成第二份字典等于在每次取用/归还的热点上
        /// 追加一次写入与一套必须与主表同步的状态。</para>
        /// </summary>
        public IReadOnlyDictionary<string, object> PoolReadOnly => _poolView ??= new PoolViewProxy(this);

        /// <summary>All 链头：诊断/调试面板遍历入口。</summary>
        public AudioClipCacheEntry FirstEntry => _allHead;

        /// <summary>LRU 链头：最久未用的可驱逐条目（驱逐与 TTL 都从头开始）。</summary>
        public AudioClipCacheEntry FirstLruEntry => _lruHead;

        /// <summary>容量上限。</summary>
        public int Capacity => _capacity;

        /// <summary>TTL 秒数（0 表示不按时间驱逐）。</summary>
        public float Ttl => _ttl;

        /// <summary>默认策略。</summary>
        public EAudioCachePolicy DefaultPolicy => _defaultPolicy;

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
        /// <param name="failureCooldownSeconds">失败地址的冷却秒数；<c>0</c> 关闭负缓存（每次都真去试）。</param>
        public void Configure(IAudioClipLeaseSource source, int capacity, float ttl, EAudioCachePolicy defaultPolicy,
            float failureCooldownSeconds = FailureCooldownSeconds)
        {
            _source = source;
            _capacity = Mathf.Max(1, capacity);
            EnsureTables();
            _ttl = Mathf.Max(0f, ttl);
            _failureCooldown = Mathf.Max(0f, failureCooldownSeconds);
            _defaultPolicy = NormalizeDefaultPolicy(defaultPolicy);
            // 重启/换后端后失败冷却不再继承上一轮——地址表可能刚被修好
            _failedUntil.Clear();
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

            _failedUntil.Clear();
            _source = null;
        }

        /// <summary>把 <see cref="EAudioCachePolicy.Default"/> 解析为配置的默认策略。</summary>
        public EAudioCachePolicy ResolvePolicy(EAudioCachePolicy policy)
        {
            return policy switch
            {
                EAudioCachePolicy.None or EAudioCachePolicy.Ttl or EAudioCachePolicy.Pin => policy,
                _ => _defaultPolicy,
            };
        }

        private static EAudioCachePolicy NormalizeDefaultPolicy(EAudioCachePolicy policy)
        {
            return policy switch
            {
                EAudioCachePolicy.None or EAudioCachePolicy.Ttl or EAudioCachePolicy.Pin => policy,
                _ => EAudioCachePolicy.Ttl,
            };
        }

        /// <summary>
        /// 地址是否处于加载失败冷却中。冷却内不再向后端取租约，也不挂等待者——
        /// 否则一个写错的事件地址每次触发播放都会完整穿一遍资源层（加载→失败→摘条目）。
        /// </summary>
        public bool IsFailureCoolingDown(string address)
        {
            if (_failureCooldown <= 0f) return false;
            if (string.IsNullOrEmpty(address) || !_failedUntil.TryGetValue(address, out var until)) return false;
            if (Time.realtimeSinceStartup < until) return true;

            _failedUntil.Remove(address);
            return false;
        }

        private void RememberFailure(string address)
        {
            if (_failureCooldown <= 0f) return;

            // 上限用容量本身兜住：错误地址理论上无上限（每条玩法数据都可能写错一个字符串），
            // 不能让这个冷却字典长成第二个泄漏源。到顶就整表重来，最坏是提前允许重试一次。
            if (_failedUntil.Count >= _capacity) _failedUntil.Clear();
            _failedUntil[address] = Time.realtimeSinceStartup + _failureCooldown;
        }

        /// <summary>清空失败冷却（服务重启/显式重置时调用）。</summary>
        public void ClearFailureCooldowns() => _failedUntil.Clear();

        /// <summary>路径是否已在缓存中加载完成。</summary>
        public bool TryGetLoaded(string address, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (!TryFindEntry(address, out var e)) return false;
            if (!e.IsLoaded) return false;
            entry = e;
            return true;
        }

        /// <summary>获取条目（不引用计数，仅查询/诊断用）。</summary>
        public bool TryGetEntry(string address, out AudioClipCacheEntry entry)
        {
            return TryFindEntry(address, out entry);
        }

        /// <summary>
        /// 声部请求 clip：命中则回调就绪；未命中则挂等待者并按需启动单飞加载。
        /// <para>同地址并发请求共用一次加载，等待者按请求顺序起播。</para>
        /// </summary>
        /// <param name="generation">声部加载世代；完成回调与之不符时落空（声部已换曲/已停播）。</param>
        /// <returns>已就绪或加载已受理返回 true；地址无效、满载或后端不可用返回 false。</returns>
        public bool RequestClip(string address, bool async, EAudioCachePolicy policy, AudioAgent agent, int generation)
        {
            AudioMainThread.AssertMainThread(nameof(RequestClip));

            if (agent == null || string.IsNullOrEmpty(address) || _source == null || _disposed)
            {
                return false;
            }

            if (IsFailureCoolingDown(address))
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
        public bool Preload(string address, EAudioCachePolicy policy = EAudioCachePolicy.Pin)
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
        public void PreloadAsync(string address, EAudioCachePolicy policy, Action<bool> completed = null)
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
            AudioMainThread.AssertMainThread(nameof(Unload));

            if (!TryFindEntry(address, out var entry)) return false;
            if (entry.RefCount != 0 || entry.Loading) return false;
            if (!force && (entry.Pinned || entry.PendingHead != null)) return false;

            _failedUntil.Remove(address);
            RemoveEntry(entry);
            return true;
        }

        /// <summary>
        /// 清空缓存。<paramref name="force"/> 连 Pin 一并清；在播引用始终不清。
        /// </summary>
        /// <remarks>force 同时清掉失败冷却：这是「配置改完了、重来一遍」的显式入口。</remarks>
        public void ClearCache(bool force = false)
        {
            AudioMainThread.AssertMainThread(nameof(ClearCache));

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

            if (force) _failedUntil.Clear();
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
        }

        /// <summary>低内存：清非 Pin、无引用缓存。</summary>
        public void OnLowMemory()
        {
            if (_disposed) return;
            ClearCache(force: false);
        }

        private static bool CanEvict(AudioClipCacheEntry entry)
            => entry.RefCount == 0 && !entry.Loading && entry.PendingHead == null;

        private bool TryPreparePreload(string address, EAudioCachePolicy policy, out AudioClipCacheEntry entry)
        {
            AudioMainThread.AssertMainThread(nameof(Preload));

            entry = null;
            if (string.IsNullOrEmpty(address) || _source == null || _disposed) return false;

            if (IsFailureCoolingDown(address))
            {
                return false;
            }

            var resolved = ResolvePolicy(policy);
            entry = GetOrCreate(address, resolved);
            if (entry == null) return false;

            UpgradePolicy(entry, resolved);
            Touch(entry);
            return true;
        }

        private AudioClipCacheEntry GetOrCreate(string address, EAudioCachePolicy policy)
        {
            int hash = HashAddress(address);
            if (TryFindEntry(address, hash, out var existing)) return existing;

            // 满载：先淘汰最久未用的无引用条目；全 Pin/全占用则拒绝新地址（宁可漏播，不可无界驻留）
            int slotIndex = AcquireSlot();
            if (slotIndex < 0) return null;

            var entry = MemoryPool.Acquire<AudioClipCacheEntry>();
            entry.Initialize(this, address, hash, policy);
            entry.SlotIndex = slotIndex;
            _slots[slotIndex] = entry;
            AddToTable(entry);
            AddToAllList(entry);
            return entry;
        }

        #region 槽表 [SLOT TABLE]

        /// <summary>
        /// 按容量铺定长槽表与 2 倍容量的开址桶。
        /// <para>用定长数组而不是 <c>Dictionary</c>：地址哈希、桶增长与 rehash 全部落在播放与驱逐的路径上，
        /// 而 Dictionary 只会长大不会缩——一个"条目数有上限"的缓存配一份无上限的桶表，上界是纸面的。</para>
        /// <para><c>Configure</c> 每次后端初始化都会重跑，容量改小于现存条目数时抬回现存数：
        /// 静默丢条目会连带把仍被声部引用的租约一起丢掉，比"这一轮容量比配置大"严重得多。</para>
        /// </summary>
        private void EnsureTables()
        {
            if (_slots != null && _slots.Length == _capacity) return;
            if (_capacity < _count) _capacity = _count;
            if (_slots == null || _count == 0)
            {
                AllocateTables();
                return;
            }

            RehashExistingIntoNewTables();
        }

        private void AllocateTables()
        {
            _slots = new AudioClipCacheEntry[_capacity];
            _freeSlots = new int[_capacity];
            _freeCount = _capacity;
            for (int i = 0; i < _capacity; i++)
            {
                // 栈式自由表：低槽位先出，冷启动时条目分布可复现
                _freeSlots[i] = _capacity - 1 - i;
            }

            int bucketCount = Mathf.Max(16, NextPowerOfTwo(_capacity << 1));
            _buckets = new int[bucketCount];
            for (int i = 0; i < bucketCount; i++) _buckets[i] = -1;
            _bucketMask = bucketCount - 1;
            _count = 0;
        }

        /// <summary>
        /// 容量变更时把现存条目重新落进新表。走 All 链而不是旧桶：
        /// All/LRU 两条链与槽位无关，换表期间保持完整，因此不需要任何临时数组。
        /// </summary>
        private void RehashExistingIntoNewTables()
        {
            AllocateTables();
            for (var entry = _allHead; entry != null; entry = entry.AllNext)
            {
                entry.SlotIndex = _freeSlots[--_freeCount];
                _slots[entry.SlotIndex] = entry;
                AddToTable(entry);
            }
        }

        /// <summary>取一只空槽；满载时先驱逐 LRU 头，全 Pin/全占用则返回 -1（新地址判负）。</summary>
        private int AcquireSlot()
        {
            if (_freeCount > 0) return _freeSlots[--_freeCount];
            if (_lruHead == null) return -1;

            RemoveEntry(_lruHead);
            return _freeCount > 0 ? _freeSlots[--_freeCount] : -1;
        }

        private static int HashAddress(string address)
        {
            unchecked
            {
                // djb2：不取 string.GetHashCode——它按进程随机化，同一份包两次启动的桶分布都不一样，
                // 复现线上"某个地址总在冲突链尾"这类问题时需要对齐的分布。
                int hash = 5381;
                for (int i = 0; i < address.Length; i++) hash = ((hash << 5) + hash) ^ address[i];
                return hash & 0x7fffffff;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        private bool TryFindEntry(string address, out AudioClipCacheEntry entry)
            => TryFindEntry(address, HashAddress(address), out entry);

        /// <summary>桶内按侵入式索引链走查；地址比较走 Ordinal。</summary>
        private bool TryFindEntry(string address, int hash, out AudioClipCacheEntry entry)
        {
            entry = null;
            if (_buckets == null || string.IsNullOrEmpty(address)) return false;

            int slotIndex = _buckets[hash & _bucketMask];
            while (slotIndex >= 0)
            {
                var current = _slots[slotIndex];
                if (current.AddressHash == hash && string.Equals(current.Address, address, StringComparison.Ordinal))
                {
                    entry = current;
                    return true;
                }

                slotIndex = current.HashNextIndex;
            }

            return false;
        }

        private void AddToTable(AudioClipCacheEntry entry)
        {
            int bucket = entry.AddressHash & _bucketMask;
            entry.HashNextIndex = _buckets[bucket];
            _buckets[bucket] = entry.SlotIndex;
            _count++;
        }

        /// <summary>摘除索引链。槽位由调用方归还，因此这里只负责链与计数。</summary>
        private void RemoveFromTable(AudioClipCacheEntry entry)
        {
            int bucket = entry.AddressHash & _bucketMask;
            int currentIndex = _buckets[bucket];
            int previousIndex = -1;
            while (currentIndex >= 0)
            {
                var current = _slots[currentIndex];
                if (ReferenceEquals(current, entry))
                {
                    if (previousIndex < 0) _buckets[bucket] = current.HashNextIndex;
                    else _slots[previousIndex].HashNextIndex = current.HashNextIndex;

                    current.HashNextIndex = -1;
                    _count--;
                    return;
                }

                previousIndex = currentIndex;
                currentIndex = current.HashNextIndex;
            }
        }

        private void ReleaseSlot(AudioClipCacheEntry entry)
        {
            int slotIndex = entry.SlotIndex;
            if (slotIndex < 0) return;

            _slots[slotIndex] = null;
            _freeSlots[_freeCount++] = slotIndex;
            entry.SlotIndex = -1;
        }

        /// <summary>该条目是否留在池里（已加载 + 留池策略或 Pin）——<see cref="PoolViewProxy"/> 的可见性判据。</summary>
        internal static bool IsPoolVisible(AudioClipCacheEntry entry)
            => entry != null && entry.Address != null && entry.IsLoaded && (entry.Pinned || entry.CacheAfterUse);

        #endregion 槽表 [SLOT TABLE]

        /// <summary>策略只升不降：Pin 永不被降级，None 不会把已缓存的条目改成不缓存。</summary>
        private void UpgradePolicy(AudioClipCacheEntry entry, EAudioCachePolicy policy)
        {
            if (policy == EAudioCachePolicy.None) return;
            if (entry.CachePolicy == EAudioCachePolicy.Pin) return;

            if (policy == EAudioCachePolicy.Pin)
            {
                entry.CachePolicy = EAudioCachePolicy.Pin;
                RemoveFromLru(entry);
                return;
            }

            if (entry.CachePolicy == EAudioCachePolicy.None)
            {
                entry.CachePolicy = EAudioCachePolicy.Ttl;
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
                // 回调携带 entry 引用：迟到的完成由 version 与槽位身份校验拦下
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
            if (!AudioMainThread.IsMainThread)
            {
                // 兜底：租约来源若不在主线程回调（未来的纯后台后端，或误加 ConfigureAwait(false)），
                // 把结果转投主线程；投不出去（调度器已停机）就当场归还租约。
                // 绝不在后台线程动 LRU/All 链与引用计数——那类问题线上表现为偶发错音，几乎无法归因。
                if (MainThreadDispatcher.TryPost(() => OnLoadCompleted(entry, version, lease))) return false;

                lease.Release();
                AudioWarnOnce.Error("cache.offthread-completion",
                    "[AudioClipCache] 加载完成回调来自非主线程，且主线程调度器已停机；租约已就地归还。");
                return false;
            }

            // 世代/身份校验：条目已被驱逐、或槽位已复用给另一地址时，本次结果作废并归还租约。
            // 走槽位而不是地址：迟到续体本来就不该再有一次字符串哈希与查表。
            if (entry.SlotIndex < 0 || entry.Version != version ||
                !ReferenceEquals(_slots[entry.SlotIndex], entry))
            {
                lease.Release();
                return false;
            }

            entry.Loading = false;
            bool success = lease.IsValid;
            if (!success)
            {
                lease.Release();
                RememberFailure(entry.Address);
            }

            entry.Lease.Release();
            entry.Lease = success ? lease : default;
            entry.Clip = success ? lease.Clip : null;

            // 派发期间自持一份引用：等待者回调里的请求/卸载不能中途把本条目挤成负引用或被驱逐
            Retain(entry);
            AudioLoadRequest callbacks = CompletePending(entry, success);
            Release(entry);
            CompletePreloads(callbacks, success);

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

            RemoveFromTable(entry);
            RemoveFromLru(entry);
            RemoveFromAllList(entry);
            ReleaseSlot(entry);

            AudioLoadRequest callbacks = CompletePending(entry, false);
            MemoryPool.Release(entry);
            CompletePreloads(callbacks, false);
        }

        /// <summary>
        /// 留池视图的只读实现：每次读都从槽表现算。
        /// <para>刻意不做成维护型的镜像表——枚举它只有调试面板一路，代价是每次枚举分配一份快照，
        /// 而镜像表的代价是每次取用/归还/驱逐都要多写一份状态并与主表保持一致。</para>
        /// </summary>
        private sealed class PoolViewProxy : IReadOnlyDictionary<string, object>
        {
            private readonly AudioClipCache _cache;

            public PoolViewProxy(AudioClipCache cache) => _cache = cache;

            public int Count
            {
                get
                {
                    int count = 0;
                    for (var entry = _cache._allHead; entry != null; entry = entry.AllNext)
                    {
                        if (IsPoolVisible(entry)) count++;
                    }

                    return count;
                }
            }

            public IEnumerable<string> Keys => SnapshotAddresses();

            public IEnumerable<object> Values => SnapshotLeases();

            public bool ContainsKey(string address)
                => _cache.TryFindEntry(address, out var entry) && IsPoolVisible(entry);

            public bool TryGetValue(string address, out object value)
            {
                value = null;
                if (!_cache.TryFindEntry(address, out var entry) || !IsPoolVisible(entry)) return false;
                value = entry.Lease;
                return true;
            }

            public object this[string address]
                => TryGetValue(address, out var value) ? value : throw new KeyNotFoundException(address);

            /// <summary>枚举即快照：先摘出地址串，避免调用方在枚举里卸载条目把链走断。</summary>
            public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            {
                var addresses = SnapshotAddresses();
                var leases = SnapshotLeases();
                for (int i = 0; i < addresses.Length; i++)
                {
                    yield return new KeyValuePair<string, object>(addresses[i], leases[i]);
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private string[] SnapshotAddresses()
            {
                var result = new string[Count];
                int index = 0;
                for (var entry = _cache._allHead; entry != null; entry = entry.AllNext)
                {
                    if (!IsPoolVisible(entry)) continue;
                    if (index < result.Length) result[index++] = entry.Address;
                }

                return result;
            }

            private object[] SnapshotLeases()
            {
                var result = new object[Count];
                int index = 0;
                for (var entry = _cache._allHead; entry != null; entry = entry.AllNext)
                {
                    if (!IsPoolVisible(entry)) continue;
                    if (index < result.Length) result[index++] = entry.Lease;
                }

                return result;
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
