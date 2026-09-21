using System;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 过期回收——时间轮走查、空闲容量淘汰与资源记录释放。
    /// </summary>
    partial class YooAssetHandler
    {
        internal override void ProcessResourceMaintenance(float unscaledTime, int maxCount)
        {
            // 销毁态兜底回收先于预算判定：没有到期记录可处理时，被销毁对象的槽位照样要收。
            _bindingService?.ProcessDestroyedObjects();

            if ((_keepAliveBuckets != null || _idleBuckets != null) && maxCount > 0)
            {
                int currentTick = ToKeepAliveTick(unscaledTime);
                int processed = ProcessDueKeepAliveBuckets(currentTick, maxCount);
                if (processed < maxCount)
                {
                    ProcessDueIdleBuckets(currentTick, maxCount - processed);
                }
            }

            // 容量淘汰排在轮盘走查之后：走查途中同步摘除会让已捕获的 next 指针失效、整桶被跳过。
            if (_idleCapacityTrimPending)
            {
                TrimIdleAssetCapacity();
            }
        }

        /// <summary>
        /// 把空闲记录数压回 <c>IdleAssetCapacity</c> 以内：每轮淘汰过期刻度最早（即最长空闲）的一条。
        /// </summary>
        private void TrimIdleAssetCapacity()
        {
            _idleCapacityTrimPending = false;

            if (_unusedAssetCandidates == null)
            {
                return;
            }

            while (_unusedAssetCandidateCount > _idleAssetCapacity)
            {
                int candidateCount = _unusedAssetCandidateCount;
                int victimIndex = -1;
                int victimExpireTick = int.MaxValue;

                for (int i = 0; i < candidateCount; i++)
                {
                    int assetId = _unusedAssetCandidates[i];
                    if (!IsValidAssetId(assetId))
                    {
                        continue;
                    }

                    ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                    if (slot.ExpireQueueKind != 2 || slot.IdleExpireTick >= victimExpireTick)
                    {
                        continue;
                    }

                    victimExpireTick = slot.IdleExpireTick;
                    victimIndex = i;
                }

                if (victimIndex < 0)
                {
                    return;
                }

                int victimId = _unusedAssetCandidates[victimIndex];
                ref AssetSlot victim = ref GetAssetSlotRef(victimId);
                uint victimGeneration = victim.Generation;
                victim.IdleReleaseRequested = 1;
                ReleaseAssetStorage(victimId, victimGeneration);

                if (_unusedAssetCandidateCount >= candidateCount)
                {
                    // 仍被引用而未能释放：留给到期轮盘，不在此原地打转。
                    return;
                }
            }
        }

        private int ProcessDueKeepAliveBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastKeepAliveProcessTick < 0 || currentTick - _lastKeepAliveProcessTick > KEEP_ALIVE_BUCKET_COUNT)
            {
                _lastKeepAliveProcessTick = currentTick - KEEP_ALIVE_BUCKET_COUNT;
            }

            int processed = 0;
            while (_lastKeepAliveProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastKeepAliveProcessTick + 1;
                int bucketProcessed = ProcessKeepAliveBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastKeepAliveProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessDueIdleBuckets(int currentTick, int maxCount)
        {
            if (maxCount <= 0)
            {
                return 0;
            }

            if (_lastIdleProcessTick < 0 || currentTick - _lastIdleProcessTick > IDLE_BUCKET_COUNT)
            {
                _lastIdleProcessTick = currentTick - IDLE_BUCKET_COUNT;
            }

            int processed = 0;
            while (_lastIdleProcessTick < currentTick && processed < maxCount)
            {
                int bucketTick = _lastIdleProcessTick + 1;
                int bucketProcessed = ProcessIdleBucket(bucketTick, currentTick, maxCount - processed, out bool completed);
                processed += bucketProcessed;
                if (!completed)
                {
                    break;
                }

                _lastIdleProcessTick = bucketTick;
            }

            return processed;
        }

        private int ProcessKeepAliveBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_keepAliveBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (KEEP_ALIVE_BUCKET_COUNT - 1);
            if (bucket >= _keepAliveBuckets.Length)
            {
                LogUtility.Error("[Resource][Wheel] KA bucket OOB: bucketTick={0} bucket={1} len={2} lastTick={3} currentTick={4} nextIndex={5} pages={6}",
                    bucketTick, bucket, _keepAliveBuckets.Length, _lastKeepAliveProcessTick, currentTick,
                    _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0);
                return 0;
            }

            int processed = 0;
            int current = _keepAliveBuckets[bucket];
            while (current >= 0)
            {
                if (current >= _assetSlotNextIndex)
                {
                    LogUtility.Error("[Resource][Wheel] KA zombie id: id={0} bucket={1} nextIndex={2} pages={3} head={4}",
                        current, bucket, _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0, _keepAliveBuckets[bucket]);
                    _keepAliveBuckets[bucket] = -1;
                    break;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 1 && slot.KeepAliveExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromKeepAliveBucket(current, ref slot);
                    if (slot.KeepAliveRefCount > 0)
                    {
                        slot.KeepAliveRefCount = 0;
                    }

                    UpdateAssetStateAndIdleQueue(current, ref slot);
                    processed++;
                }

                current = next;
            }

            return processed;
        }

        private int ProcessIdleBucket(int bucketTick, int currentTick, int maxCount, out bool completed)
        {
            completed = true;
            if (_idleBuckets == null || maxCount <= 0)
            {
                return 0;
            }

            int bucket = bucketTick & (IDLE_BUCKET_COUNT - 1);
            if (bucket >= _idleBuckets.Length)
            {
                LogUtility.Error("[Resource][Wheel] Idle bucket OOB: bucketTick={0} bucket={1} len={2} lastTick={3} currentTick={4} nextIndex={5} pages={6}",
                    bucketTick, bucket, _idleBuckets.Length, _lastIdleProcessTick, currentTick,
                    _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0);
                return 0;
            }

            int processed = 0;
            int current = _idleBuckets[bucket];
            while (current >= 0)
            {
                if (current >= _assetSlotNextIndex)
                {
                    LogUtility.Error("[Resource][Wheel] Idle zombie id: id={0} bucket={1} nextIndex={2} pages={3} head={4}",
                        current, bucket, _assetSlotNextIndex, _assetSlotPages != null ? _assetSlotPages.Length : 0, _idleBuckets[bucket]);
                    _idleBuckets[bucket] = -1;
                    break;
                }

                ref AssetSlot slot = ref GetAssetSlotRef(current);
                int next = slot.ExpireQueueNext;
                if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick <= currentTick)
                {
                    if (processed >= maxCount)
                    {
                        completed = false;
                        break;
                    }

                    RemoveFromIdleBucket(current, ref slot);
                    if (HasNoResourceRefs(ref slot))
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

        internal override int ReleaseAllUnusedAssetRecords()
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

        internal override void ForceReleaseAllAssetRecords()
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
                UnlinkAssetByUnityObject(i, ref slot);
                ClearAssetSlot(ref slot, preserveGeneration: true);
                FreeAssetSlot(i);
            }

            ReleaseAllResourceKeysFromMap(_assetRecordsByKey);
            _assetRecordsByKey.Clear();
            _assetRecordByLoadKeyId.Clear();
            _assetRecordHeadByUnityObjectId.Clear();
            _unusedAssetCandidateCount = 0;

            _leaseSlotNextIndex = 0;
            _leaseSlotFreeHead = -1;
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
            UnlinkAssetByUnityObject(assetId, ref slot);
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
                   slot.LegacyDirectRefCount == 0 &&
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

            if (slot.DirectRefCount + slot.LegacyDirectRefCount + slot.BindingRefCount > 0)
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
                AddUnusedAssetCandidate(assetId, ref slot);
                EnterIdle(assetId, ref slot);

                if (_unusedAssetCandidateCount > _idleAssetCapacity)
                {
                    _idleCapacityTrimPending = true;
                }
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
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

            int expireTick = ToKeepAliveTick(Time.unscaledTime) + Mathf.Max(0, Mathf.CeilToInt(_idleAssetExpireTime));
            if (slot.ExpireQueueKind == 2 && slot.IdleExpireTick == expireTick)
            {
                return;
            }

            RemoveFromExpiryQueue(assetId, ref slot);
            slot.IdleExpireTick = expireTick;
            if (_idleBuckets == null || _idleBuckets.Length != IDLE_BUCKET_COUNT)
            {
                _idleBuckets = new int[IDLE_BUCKET_COUNT];
                for (int i = 0; i < IDLE_BUCKET_COUNT; i++)
                {
                    _idleBuckets[i] = -1;
                }
            }

            int bucket = expireTick & (IDLE_BUCKET_COUNT - 1);
            slot.ExpireQueueBucket = bucket;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _idleBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _idleBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 2;
        }

        private void AddToKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            if (_keepAliveBuckets == null || _keepAliveBuckets.Length != KEEP_ALIVE_BUCKET_COUNT)
            {
                _keepAliveBuckets = new int[KEEP_ALIVE_BUCKET_COUNT];
                for (int i = 0; i < KEEP_ALIVE_BUCKET_COUNT; i++)
                {
                    _keepAliveBuckets[i] = -1;
                }
            }

            RemoveFromKeepAliveBucket(assetId, ref slot);
            int bucket = slot.KeepAliveExpireTick & (KEEP_ALIVE_BUCKET_COUNT - 1);
            slot.ExpireQueueBucket = bucket;
            slot.ExpireQueuePrev = -1;
            slot.ExpireQueueNext = _keepAliveBuckets[bucket];
            if (slot.ExpireQueueNext >= 0)
            {
                ref AssetSlot next = ref GetAssetSlotRef(slot.ExpireQueueNext);
                next.ExpireQueuePrev = assetId;
            }

            _keepAliveBuckets[bucket] = assetId;
            slot.ExpireQueueKind = 1;
        }

        private void RemoveFromExpiryQueue(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind == 1)
            {
                RemoveFromKeepAliveBucket(assetId, ref slot);
            }
            else if (slot.ExpireQueueKind == 2)
            {
                RemoveFromIdleBucket(assetId, ref slot);
            }
        }

        private void RemoveFromKeepAliveBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 1 || _keepAliveBuckets == null)
            {
                return;
            }

            // 链接时已存桶号：tick 可能在链接后被更新，反推桶号会定位到错误桶导致 unlink 静默失败、桶头悬挂僵尸 id。
            int bucket = slot.ExpireQueueBucket;
            if (bucket < 0 || bucket >= KEEP_ALIVE_BUCKET_COUNT)
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
            else if (_keepAliveBuckets[bucket] == assetId)
            {
                _keepAliveBuckets[bucket] = next;
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

        private void RemoveFromIdleBucket(int assetId, ref AssetSlot slot)
        {
            if (slot.ExpireQueueKind != 2 || _idleBuckets == null)
            {
                return;
            }

            // 同 KeepAlive：读链接时存储的桶号，禁止由当前 tick 反推。
            int bucket = slot.ExpireQueueBucket;
            if (bucket < 0 || bucket >= IDLE_BUCKET_COUNT)
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
            else if (_idleBuckets[bucket] == assetId)
            {
                _idleBuckets[bucket] = next;
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
                return;
            }

            if (_unusedAssetCandidates == null)
            {
                _unusedAssetCandidates = new int[Math.Max(16, _assetRecordCapacity)];
            }
            else if (_unusedAssetCandidateCount >= _unusedAssetCandidates.Length)
            {
                Array.Resize(ref _unusedAssetCandidates, _unusedAssetCandidates.Length << 1);
            }

            slot.UnusedCandidateIndex = _unusedAssetCandidateCount;
            _unusedAssetCandidates[_unusedAssetCandidateCount++] = assetId;
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
                }
            }

            if (IsValidAssetId(removedAssetId))
            {
                ref AssetSlot removedSlot = ref GetAssetSlotRef(removedAssetId);
                removedSlot.UnusedCandidateIndex = -1;
            }
        }

        private static int ToKeepAliveTick(float unscaledTime)
        {
            return Mathf.Max(0, Mathf.FloorToInt(unscaledTime));
        }
    }
}
