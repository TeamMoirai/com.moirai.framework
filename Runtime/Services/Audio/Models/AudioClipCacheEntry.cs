using System;
using System.Threading;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioClip 缓存条目：租约 + 引用计数 + LRU/All 双链 + 挂起加载回调。
    /// </summary>
    internal sealed class AudioClipCacheEntry : MemoryObject
    {
        public AudioClipCache Owner;
        public ulong Version;
        public CancellationTokenSource Cancellation;
        public string Address;
        public AudioClipLease Lease;

        /// <summary>
        /// <see cref="Lease"/> 的**一次性装箱副本**，专供 <c>AssetHandlePool</c> 只读视图使用。
        /// <para>视图每次刷新若直接写 <see cref="Lease"/>，就会在每次取用/归还的热点上重复装箱；
        /// 这里让装箱只发生在租约换手的那一次。</para>
        /// </summary>
        public object LeaseBoxed;
        public AudioClip Clip;
        public AudioLoadRequest PendingHead;
        public AudioLoadRequest PendingTail;
        public AudioClipCacheEntry LruPrev;
        public AudioClipCacheEntry LruNext;
        public AudioClipCacheEntry AllPrev;
        public AudioClipCacheEntry AllNext;
        public int HashNextIndex = -1;
        public int RefCount;
        public int AddressHash;
        public AudioCachePolicy CachePolicy;
        public bool Loading;
        public bool InLru;
        public float LastUseTime;

        public bool Pinned => CachePolicy == AudioCachePolicy.Pin;
        public bool CacheAfterUse => CachePolicy is AudioCachePolicy.Ttl or AudioCachePolicy.Pin;
        public bool IsLoaded => Clip != null && Lease.IsValid && !Loading;

        public void Initialize(AudioClipCache owner, string address, int addressHash, AudioCachePolicy cachePolicy)
        {
            Version++;
            Owner = owner;
            Address = address;
            AddressHash = addressHash;
            HashNextIndex = -1;
            CachePolicy = cachePolicy;
            LastUseTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 追加等待者到挂起队列尾（FIFO，保证同地址多声部按请求顺序起播）。
        /// </summary>
        public void AddPending(AudioLoadRequest request)
        {
            request.Entry = this;
            request.Prev = PendingTail;
            request.Next = null;
            if (PendingTail == null)
            {
                PendingHead = request;
                PendingTail = request;
                return;
            }

            PendingTail.Next = request;
            PendingTail = request;
        }

        /// <summary>
        /// 从本条目摘除等待者；非本条目的请求（已摘除或串到别处）返回 false，避免误删。
        /// </summary>
        public bool RemovePending(AudioLoadRequest request)
        {
            if (request == null || !ReferenceEquals(request.Entry, this)) return false;

            AudioLoadRequest prev = request.Prev;
            AudioLoadRequest next = request.Next;
            if (prev != null) prev.Next = next;
            else PendingHead = next;
            if (next != null) next.Prev = prev;
            else PendingTail = prev;

            request.Entry = null;
            request.Prev = null;
            request.Next = null;
            return true;
        }

        public override void Clear()
        {
            Owner = null;
            if (Loading)
            {
                Cancellation?.Cancel();
            }

            Cancellation?.Dispose();
            Cancellation = null;
            Lease.Release();
            Lease = default;
            LeaseBoxed = null;
            Address = null;
            Clip = null;
            // 条目被丢弃时仍挂着等待者：正常路径已由缓存统一通知，此处兜底归还，避免请求节点脱离池
            AudioLoadRequest request = PendingHead;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                MemoryPool.Release(request);
                request = next;
            }

            PendingHead = null;
            PendingTail = null;
            LruPrev = null;
            LruNext = null;
            AllPrev = null;
            AllNext = null;
            HashNextIndex = -1;
            RefCount = 0;
            AddressHash = 0;
            CachePolicy = AudioCachePolicy.Default;
            Loading = false;
            InLru = false;
            LastUseTime = 0f;
        }
    }
}
