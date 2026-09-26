using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核的过期侧——两座侵入式时间轮（KeepAlive / Idle）、未用候选表与容量淘汰。
    /// <para>摘链一律按槽里存下的桶号走、绝不从当前 tick 反推，走查途中不得同步摘除；
    /// 这两条各记过一次真实事故，改前先读方法上的注释。</para>
    /// </summary>
    internal sealed partial class ResourceRecordStore
    {
        // 一趟容量淘汰最多摘掉几条。挑受害者是整表扫（候选表无序、又不是轮盘序），
        // 而外层的 while 会一路摘到不超限为止，所以不设上限就是每受害者 O(n) 的一帧突发：
        // 空闲记录 300 条、容量从 256 调到 8，那是一帧里 292×300 次槽位读。
        // 上限只限制"这一帧做多少"，不改变淘汰次序——没做完就把请求位留着，下一帧接着摘。
        private const int IDLE_TRIM_VICTIMS_PER_PASS = 8;

        /// <summary>容量被调小后请求一次淘汰：不当场做，交给下一帧的维护走查。</summary>
        internal void RequestIdleCapacityTrim()
        {
            _idleCapacityTrimPending = true;
        }

        /// <summary>
        /// 淘汰请求位。运行期只有 <c>ProcessResourceMaintenance</c> 读它，这里另开一个读数给测试：
        /// "预算用尽时把请求位留回"是这条路径唯一的续跑保证，漏掉就静默停在超限状态。
        /// </summary>
        internal bool IdleCapacityTrimPending => _idleCapacityTrimPending;

        /// <summary>
        /// 按最小堆弹出空闲最久（<see cref="AssetSlot.IdleExpireTick"/> 最小）的受害者淘汰。
        /// <para>候选表是按过期刻度的二叉最小堆：<c>UnusedCandidateIndex</c> 即堆下标。
        /// 每趟最多 <paramref name="maxVictims"/> 条，没做完把请求位留回。</para>
        /// </summary>
        internal void TrimIdleAssetCapacity(int maxVictims)
        {
            _idleCapacityTrimPending = false;

            if (_unusedAssetCandidates == null)
            {
                return;
            }

            for (int trimmed = 0; _unusedAssetCandidateCount > Host.IdleAssetCapacity; trimmed++)
            {
                if (trimmed >= maxVictims)
                {
                    // 预算用尽而非摘完：把请求位留回，下一帧继续，否则会静默停在超限状态。
                    _idleCapacityTrimPending = true;
                    return;
                }

                if (!TryPeekLongestIdle(out int victimIndex, out int victimId, out int victimExpireTick))
                {
                    return;
                }

                ref AssetSlot victim = ref GetAssetSlotRef(victimId);
                // 堆顶若已被改成非 idle（竞态），弹掉重试；合法受害者按 IdleExpireTick 从旧到新。
                if (victim.ExpireQueueKind != WHEEL_KIND_IDLE || victim.IdleExpireTick != victimExpireTick)
                {
                    RemoveUnusedAssetCandidateAt(victimIndex);
                    continue;
                }

                uint victimGeneration = victim.Generation;
                victim.IdleReleaseRequested = 1;
                int candidateCount = _unusedAssetCandidateCount;
                ReleaseAssetStorage(victimId, victimGeneration);

                if (_unusedAssetCandidateCount >= candidateCount)
                {
                    // 仍被引用而未能释放：留给到期轮盘，不在此原地打转。
                    return;
                }
            }
        }

        private bool TryPeekLongestIdle(out int heapIndex, out int assetId, out int expireTick)
        {
            // 最小堆根即空闲最久者，O(1)。
            heapIndex = 0;
            assetId = -1;
            expireTick = int.MaxValue;
            if (_unusedAssetCandidateCount <= 0)
            {
                return false;
            }

            int id = _unusedAssetCandidates[0];
            if (!IsValidAssetId(id))
            {
                RemoveUnusedAssetCandidateAt(0);
                return TryPeekLongestIdle(out heapIndex, out assetId, out expireTick);
            }

            ref AssetSlot slot = ref GetAssetSlotRef(id);
            if (slot.ExpireQueueKind != WHEEL_KIND_IDLE)
            {
                RemoveUnusedAssetCandidateAt(0);
                return TryPeekLongestIdle(out heapIndex, out assetId, out expireTick);
            }

            assetId = id;
            expireTick = slot.IdleExpireTick;
            return true;
        }

        private int ProcessDueWheelBuckets(int[] buckets, ref int lastProcessTick, int queueKind,
            int currentTick, int maxCount)
        {
            if (maxCount <= 0 || buckets == null)
            {
                return 0;
            }

            int span = buckets.Length;
            if (lastProcessTick < 0 || currentTick - lastProcessTick > span)
            {
                lastProcessTick = currentTick - span;
            }

            int processed = 0;
            while (lastProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = lastProcessTick + 1;
                int bucketProcessed = ProcessWheelBucket(buckets, queueKind, bucketTick, currentTick,
                    maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                lastProcessTick = bucketTick;
            }

            return processed;
        }

        /// <summary>
        /// 两座时间轮共用的桶走查。KeepAlive（kind=1）到期清保活计数后转状态；Idle（kind=2）到期无引用则释放。
        /// <para>摘链一律按槽里存下的桶号（<see cref="RemoveFromWheel"/>），走查途中只记 next、不在此同步摘除未到期节点。</para>
        /// </summary>
        private int ProcessWheelBucket(int[] buckets, int queueKind, int bucketTick, int currentTick,
            int maxCount, out bool completed)
        {
            completed = true;
            if (buckets == null || maxCount <= 0)
            {
                return 0;
            }

            string logName = queueKind == WHEEL_KIND_KEEP_ALIVE ? "KA" : "Idle";
            int lastProcessTick = queueKind == WHEEL_KIND_KEEP_ALIVE ? _lastKeepAliveProcessTick : _lastIdleProcessTick;
            int bucket = bucketTick & (buckets.Length - 1);
            if (bucket >= buckets.Length)
            {
                LogUtility.Error("[Resource][Wheel] {0} bucket OOB: bucketTick={1} bucket={2} len={3} lastTick={4} currentTick={5} nextIndex={6} pages={7}",
                    logName, bucketTick, bucket, buckets.Length, lastProcessTick, currentTick,
                    _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0);
                return 0;
            }

            int processed = 0;
            int current = buckets[bucket];
            while (current >= 0)
            {
                if (current >= _assetSlotNextIndex)
                {
                    LogUtility.Error("[Resource][Wheel] {0} zombie id: id={1} bucket={2} nextIndex={3} pages={4} head={5}",
                        logName, current, bucket, _assetSlotNextIndex,
                        _assetSlotPages != null ? _assetSlotPages.Length : 0, buckets[bucket]);
                    buckets[bucket] = -1;
                    break;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                int expireTick = GetWheelExpireTick(ref slot, queueKind);
                if (slot.ExpireQueueKind == queueKind && expireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromWheel(buckets, queueKind, current, ref slot);
                    if (queueKind == WHEEL_KIND_KEEP_ALIVE)
                    {
                        if (slot.KeepAliveRefCount > 0)
                        {
                            slot.KeepAliveRefCount = 0;
                        }

                        UpdateAssetStateAndIdleQueue(current, ref slot);
                    }
                    else if (HasNoResourceRefs(ref slot))
                    {
                        slot.IdleReleaseRequested = 1;
                        ReleaseAssetStorage(current, slot.Generation);
                    }
                    else
                    {
                        UpdateAssetStateAndIdleQueue(current, ref slot);
                    }

                    processed++;
                }

                current = next;
            }

            return processed;
        }

        private static int GetWheelExpireTick(ref AssetSlot slot, int queueKind)
        {
            return queueKind == WHEEL_KIND_KEEP_ALIVE ? slot.KeepAliveExpireTick : slot.IdleExpireTick;
        }

        private void ReleaseAssetStorage(int assetId, uint generation)
        {
            if (!IsValidAssetId(assetId))
            {
                return;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            if (slot.Generation != generation || slot.State == EResourceAssetState.Released)
            {
                return;
            }

            if (!HasNoResourceRefs(ref slot) || slot.IdleReleaseRequested == 0)
            {
                UpdateAssetStateAndIdleQueue(assetId, ref slot);
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            RemoveUnusedAssetCandidate(assetId, ref slot);
            DisposeAssetSlotHandle(ref slot);
            ulong key = slot.Key;
            _assetRecordsByKey.Remove(key);
            ReleaseResourceKey(key);
            if (slot.LoadKeyId > 0)
            {
                _assetRecordByLoadKeyId.Remove((ulong)slot.LoadKeyId);
            }

            ClearAssetSlot(ref slot, preserveGeneration: true);
            FreeAssetSlot(assetId);
        }

        private static bool HasNoResourceRefs(ref AssetSlot slot)
        {
            return slot.DirectRefCount == 0 &&
                   slot.BindingRefCount == 0 &&
                   slot.KeepAliveRefCount == 0;
        }

        private void UpdateAssetState(ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                slot.State = EResourceAssetState.Released;
                return;
            }

            if (slot.DirectRefCount + slot.BindingRefCount > 0)
            {
                slot.State = EResourceAssetState.Active;
                return;
            }

            slot.State = slot.KeepAliveRefCount > 0
                ? EResourceAssetState.KeepAlive
                : EResourceAssetState.Idle;
        }

        private void UpdateAssetStateAndIdleQueue(int assetId, ref AssetSlot slot)
        {
            UpdateAssetState(ref slot);
            if (slot.State == EResourceAssetState.Idle)
            {
                slot.IdleReleaseRequested = 0;
                // 先定过期刻度再入堆：候选表按 IdleExpireTick 排序，刻度未定就插入会堆序错乱。
                EnterIdle(assetId, ref slot);
                AddUnusedAssetCandidate(assetId, ref slot);

                if (_unusedAssetCandidateCount > Host.IdleAssetCapacity)
                {
                    _idleCapacityTrimPending = true;
                }
            }
            else if (slot.ExpireQueueKind == WHEEL_KIND_IDLE)
            {
                RemoveFromWheel(_idleBuckets, WHEEL_KIND_IDLE, assetId, ref slot);
                slot.IdleReleaseRequested = 0;
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
            else
            {
                RemoveUnusedAssetCandidate(assetId, ref slot);
            }
        }

        private void EnterIdle(int assetId, ref AssetSlot slot)
        {
            if (!IsSlotHandleValid(ref slot))
            {
                return;
            }

            int expireTick = ToKeepAliveTick(Time.unscaledTime) + Mathf.Max(0, Mathf.CeilToInt(Host.IdleAssetExpireTime));
            if (slot.ExpireQueueKind == WHEEL_KIND_IDLE && slot.IdleExpireTick == expireTick)
            {
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            slot.IdleExpireTick = expireTick;
            ScheduleOnWheel(ref _idleBuckets, WHEEL_KIND_IDLE, assetId, ref slot, expireTick);
        }

        private void AddToKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            ScheduleOnWheel(ref _keepAliveBuckets, WHEEL_KIND_KEEP_ALIVE, assetId, ref slot, slot.KeepAliveExpireTick);
        }

        /// <summary>入轮：同一算法服务两座轮，仅队列种类与过期刻度来源不同。</summary>
        private void ScheduleOnWheel(ref int[] buckets, int queueKind, int assetId, ref AssetSlot slot, int expireTick)
        {
            EnsureWheelBuckets(ref buckets);
            RemoveFromWheel(buckets, queueKind, assetId, ref slot);
            int bucket = expireTick & (buckets.Length - 1);
            slot.ExpireQueueBucket = bucket;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = buckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            buckets[bucket] = assetId;
            slot.ExpireQueueKind = queueKind;
        }

        private static void EnsureWheelBuckets(ref int[] buckets)
        {
            if (buckets != null && buckets.Length == EXPIRY_WHEEL_BUCKET_COUNT)
            {
                return;
            }

            buckets = new int[EXPIRY_WHEEL_BUCKET_COUNT];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = -1;
            }
        }

        private void RemoveFromExpiryQueue(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind == WHEEL_KIND_KEEP_ALIVE)
            {
                RemoveFromWheel(_keepAliveBuckets, WHEEL_KIND_KEEP_ALIVE, assetId, ref slot);
            }
            else if (slot.ExpireQueueKind == WHEEL_KIND_IDLE)
            {
                RemoveFromWheel(_idleBuckets, WHEEL_KIND_IDLE, assetId, ref slot);
            }
        }

        /// <summary>
        /// 摘链：读链接时存下的桶号，禁止由当前 tick 反推（反推会 unlink 静默失败、桶头挂僵尸 id）。
        /// </summary>
        private void RemoveFromWheel(int[] buckets, int queueKind, int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != queueKind || buckets == null)
            {
                return;
            }

            int bucket = slot.ExpireQueueBucket;
            if (bucket < 0 || bucket >= buckets.Length)
            {
                slot.ExpireQueuePrev = -1;
                slot.ExpireQueueNext = -1;
                slot.ExpireQueueKind = 0;
                slot.ExpireQueueBucket = -1;
                return;
            }

            int prev = slot.ExpireQueuePrev;
            int next = slot.ExpireQueueNext;
            if (prev >= 0)
            {
                ref AssetSlot prevSlot = ref GetAssetSlotRef(prev);
                prevSlot.ExpireQueueNext = next;
            }
            else if (buckets[bucket] == assetId)
            {
                buckets[bucket] = next;
            }

            if (next >= 0)
            {
                ref AssetSlot nextSlot = ref GetAssetSlotRef(next);
                nextSlot.ExpireQueuePrev = prev;
            }

            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = -1;
            slot.ExpireQueueKind = 0;
            slot.ExpireQueueBucket = -1;
        }

        private void AddUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            if (slot.UnusedCandidateIndex >= 0)
            {
                // 已在堆中：刻度可能被 EnterIdle 更新，上浮/下沉校正堆序。
                SiftUnusedCandidate(slot.UnusedCandidateIndex);
                return;
            }

            if (_unusedAssetCandidates == null)
            {
                _unusedAssetCandidates = new int[Math.Max(16, Host.AssetRecordCapacity)];
            }
            else if (_unusedAssetCandidateCount >= _unusedAssetCandidates.Length)
            {
                Array.Resize(ref _unusedAssetCandidates, _unusedAssetCandidates.Length << 1);
            }

            slot.UnusedCandidateIndex = _unusedAssetCandidateCount;
            _unusedAssetCandidates[_unusedAssetCandidateCount++] = assetId;
            SiftUnusedCandidateUp(slot.UnusedCandidateIndex);
        }

        private void RemoveUnusedAssetCandidate(int assetId, ref AssetSlot slot)
        {
            int index = slot.UnusedCandidateIndex;
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                slot.UnusedCandidateIndex = -1;
                return;
            }

            if (_unusedAssetCandidates[index] != assetId)
            {
                for (int i = 0; i < _unusedAssetCandidateCount; i++)
                {
                    if (_unusedAssetCandidates[i] == assetId)
                    {
                        RemoveUnusedAssetCandidateAt(i);
                        return;
                    }
                }

                slot.UnusedCandidateIndex = -1;
                return;
            }

            RemoveUnusedAssetCandidateAt(index);
        }

        private void RemoveUnusedAssetCandidateAt(int index)
        {
            if (index < 0 || index >= _unusedAssetCandidateCount)
            {
                return;
            }

            int removedAssetId = _unusedAssetCandidates[index];
            int lastIndex = --_unusedAssetCandidateCount;
            int movedAssetId = _unusedAssetCandidates[lastIndex];
            _unusedAssetCandidates[lastIndex] = 0;
            if (index != lastIndex)
            {
                _unusedAssetCandidates[index] = movedAssetId;
                if (IsValidAssetId(movedAssetId))
                {
                    ref AssetSlot movedSlot = ref GetAssetSlotRef(movedAssetId);
                    movedSlot.UnusedCandidateIndex = index;
                    SiftUnusedCandidate(index);
                }
            }

            if (IsValidAssetId(removedAssetId))
            {
                ref AssetSlot removedSlot = ref GetAssetSlotRef(removedAssetId);
                removedSlot.UnusedCandidateIndex = -1;
            }
        }

        private void SiftUnusedCandidate(int index)
        {
            SiftUnusedCandidateUp(index);
            SiftUnusedCandidateDown(index);
        }

        private void SiftUnusedCandidateUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (CompareUnusedIdleTick(index, parent) >= 0)
                {
                    return;
                }

                SwapUnusedCandidates(index, parent);
                index = parent;
            }
        }

        private void SiftUnusedCandidateDown(int index)
        {
            int count = _unusedAssetCandidateCount;
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= count)
                {
                    return;
                }

                int smallest = left;
                int right = left + 1;
                if (right < count && CompareUnusedIdleTick(right, left) < 0)
                {
                    smallest = right;
                }

                if (CompareUnusedIdleTick(index, smallest) <= 0)
                {
                    return;
                }

                SwapUnusedCandidates(index, smallest);
                index = smallest;
            }
        }

        /// <summary>堆比较：IdleExpireTick 越小越先淘汰（空闲最久）。失效槽排最后。</summary>
        private int CompareUnusedIdleTick(int a, int b)
        {
            return GetUnusedIdleTick(a).CompareTo(GetUnusedIdleTick(b));
        }

        private int GetUnusedIdleTick(int heapIndex)
        {
            int assetId = _unusedAssetCandidates[heapIndex];
            if (!IsValidAssetId(assetId))
            {
                return int.MaxValue;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(assetId);
            return slot.ExpireQueueKind == WHEEL_KIND_IDLE ? slot.IdleExpireTick : int.MaxValue;
        }

        private void SwapUnusedCandidates(int a, int b)
        {
            int idA = _unusedAssetCandidates[a];
            int idB = _unusedAssetCandidates[b];
            _unusedAssetCandidates[a] = idB;
            _unusedAssetCandidates[b] = idA;
            if (IsValidAssetId(idA))
            {
                ref AssetSlot slotA = ref GetAssetSlotRef(idA);
                slotA.UnusedCandidateIndex = b;
            }

            if (IsValidAssetId(idB))
            {
                ref AssetSlot slotB = ref GetAssetSlotRef(idB);
                slotB.UnusedCandidateIndex = a;
            }
        }

        private static int ToKeepAliveTick(float unscaledTime)
        {
            return Mathf.Max(0, Mathf.FloorToInt(unscaledTime));
        }
        internal void ProcessResourceMaintenance(float unscaledTime, int expireBudget)
        {
            if ((_keepAliveBuckets != null || _idleBuckets != null) && expireBudget > 0)
            {
                int currentTick = ToKeepAliveTick(unscaledTime);
                int processed = ProcessDueWheelBuckets(_keepAliveBuckets, ref _lastKeepAliveProcessTick,
                    WHEEL_KIND_KEEP_ALIVE, currentTick, expireBudget);
                if (processed < expireBudget)
                {
                    ProcessDueWheelBuckets(_idleBuckets, ref _lastIdleProcessTick,
                        WHEEL_KIND_IDLE, currentTick, expireBudget - processed);
                }
            }

            // 容量淘汰排在轮盘走查之后：走查途中同步摘除会让已捕获的 next 指针失效、整桶被跳过。
            if (_idleCapacityTrimPending)
            {
                TrimIdleAssetCapacity(IDLE_TRIM_VICTIMS_PER_PASS);
            }
        }

        internal int ReleaseAllUnusedAssetRecords()
        {
            int releasedCount = 0;
            int index = 0;
            while (index < _unusedAssetCandidateCount)
            {
                int assetId = _unusedAssetCandidates[index];
                if (!IsValidAssetId(assetId))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                if (slot.Generation == 0 || slot.State == EResourceAssetState.Released ||
                    !IsSlotHandleValid(ref slot))
                {
                    RemoveUnusedAssetCandidateAt(index);
                    continue;
                }

                if (!HasNoResourceRefs(ref slot))
                {
                    RemoveUnusedAssetCandidate(assetId, ref slot);
                    continue;
                }

                slot.IdleReleaseRequested = 1;
                uint generation = slot.Generation;
                int previousCandidateCount = _unusedAssetCandidateCount;
                ReleaseAssetStorage(assetId, generation);
                if (_unusedAssetCandidateCount == previousCandidateCount && index < _unusedAssetCandidateCount &&
                    _unusedAssetCandidates[index] == assetId)
                {
                    RemoveUnusedAssetCandidateAt(index);
                }

                releasedCount++;
            }

            return releasedCount;
        }

        internal void ForceReleaseAllAssetRecords()
        {
            int total = _assetSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref AssetSlot slot = ref GetAssetSlotRef(i);
                if (slot.Generation == 0 || slot.State == EResourceAssetState.Released)
                {
                    continue;
                }

                RemoveFromExpiryQueue(i, ref slot);
                RemoveUnusedAssetCandidate(i, ref slot);
                DisposeAssetSlotHandle(ref slot);
                ClearAssetSlot(ref slot, preserveGeneration: true);
                FreeAssetSlot(i);
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _unusedAssetCandidateCount = 0;

            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
        }
    }
}
