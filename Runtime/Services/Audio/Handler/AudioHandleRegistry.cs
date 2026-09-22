using System;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频句柄注册表的声部引用契约。
    /// <para><see cref="VoiceSlot"/> 由注册表单点写入（绑定占用一只槽、卸绑置回 -1），
    /// 实现方只需保证它随声部的复用生命周期被正确复位。</para>
    /// </summary>
    internal interface IAudioVoiceRef
    {
        /// <summary>用户定义 ID。句柄按它反查，注册与卸绑都以声部上的当前值为准。</summary>
        int UserId { get; }

        /// <summary>
        /// 声部侧句柄（双向关联的 agent→handle 方向）。
        /// 由 <c>AudioHandleRegistry.Bind/Release</c> 单点写入，保证与注册表映射不失步；
        /// 实现方请勿在绑定生命周期内于其他位置赋值。
        /// </summary>
        ulong BoundHandle { get; set; }

        /// <summary>注册表槽位下标；<c>-1</c> 表示未注册。由注册表单点维护，句柄按它 O(1) 定位声部。</summary>
        int VoiceSlot { get; set; }
    }

    /// <summary>
    /// 音频句柄注册表（Unity / 中间件后端共用）。
    /// <para>职责：句柄生成与解析、句柄 → 声部绑定、用户 ID → 句柄的一一串联、按 ID 批量遍历。</para>
    /// <para>不变量：句柄 ↔ 声部为 1:1；<see cref="Bind"/> 重绑前会自动卸掉声部旧句柄；句柄低 20 位是槽位、
    /// 高 44 位是代次，代次不匹配即判假，所以被释放的槽位复用给新声部后旧句柄不会命中。</para>
    /// <para>零分配：整表是"一只声部数组 + 三只 int 数组 + 一只桶数组"，没有 <c>Dictionary</c>、
    /// 没有 <c>List&lt;ulong&gt;</c> 池，也没有遍历快照——每次播放/停播/每帧扫描都在这条链上，
    /// 托管字典的哈希与扩容尖峰会直接进播放帧。</para>
    /// </summary>
    /// <typeparam name="TVoice">后端声部类型（Unity 为 <see cref="AudioAgent"/>，中间件为私有 Voice）。</typeparam>
    internal sealed class AudioHandleRegistry<TVoice> where TVoice : class, IAudioVoiceRef
    {
        private const int SlotBits = 20;
        private const int SlotMask = (1 << SlotBits) - 1;
        private const int NoSlot = -1;
        private const int InitialCapacity = 32;

        private TVoice[] _voices = Array.Empty<TVoice>();
        private int[] _userNext = Array.Empty<int>();     // 同一 UserId 的下一条槽
        private int[] _hashNext = Array.Empty<int>();     // 同一桶内的下一条槽
        private int[] _buckets = Array.Empty<int>();      // 桶 → 首槽
        private int[] _freeSlots = Array.Empty<int>();
        private int _freeCount;
        private int _count;
        private int _bucketMask;
        private uint _generation;

        /// <summary>当前注册的句柄数量。</summary>
        public int Count => _count;

        /// <summary>只读遍历入口（结构体枚举器，foreach 装箱不发生）；遍历时不得解绑，需解绑请先收集。</summary>
        public SlotEnumerable Slots => new SlotEnumerable(this);

        /// <summary>
        /// 绑定声部并返回其服务句柄；声部若已绑在其他槽上会先完整卸绑。
        /// </summary>
        /// <returns>新句柄；槽位无法再扩时返回 <c>0</c>（调用方按播放失败处理）。</returns>
        public ulong Bind(TVoice voice)
        {
            if (voice == null) return 0UL;

            // 同声部双句柄是结构上不允许的：先把它旧的那条整体摘掉
            if (voice.BoundHandle != 0UL) Release(voice.BoundHandle, out _);

            int slot = AcquireSlot();
            if (slot < 0) return 0UL;

            _generation++;
            if (_generation == 0U) _generation = 1U;   // 0 代次让句柄退化成"仅槽位"，与未绑定态难以区分，跳过

            ulong handle = ((ulong)_generation << SlotBits) | (uint)(slot + 1);
            _voices[slot] = voice;
            voice.VoiceSlot = slot;
            voice.BoundHandle = handle;
            _count++;
            return handle;
        }

        /// <summary>
        /// 把声部挂进它当前 <see cref="IAudioVoiceRef.UserId"/> 的索引链。
        /// </summary>
        /// <remarks>必须在 <see cref="Bind"/> 之后、 UserId 定下来之后调用；同一句柄重复调用只入链一次。</remarks>
        /// <returns>成功入链返回 true；句柄无效或已在链上返回 false。</returns>
        public bool RegisterUser(ulong handle)
        {
            if (!TryGet(handle, out var voice)) return false;

            int slot = voice.VoiceSlot;
            int bucket = BucketOf(voice.UserId);
            for (int walk = _buckets[bucket]; walk != NoSlot; walk = _hashNext[walk])
            {
                if (walk == slot) return false;         // 已在链上，重复登记会自链接成环
            }

            _userNext[slot] = NoSlot;
            _hashNext[slot] = _buckets[bucket];
            _buckets[bucket] = slot;
            return true;
        }

        /// <summary>查询句柄绑定的声部（代次不符即为陈旧句柄，返回 false）。</summary>
        public bool TryGet(ulong handle, out TVoice voice)
        {
            voice = null;
            if (handle == 0UL) return false;

            int slot = (int)(handle & SlotMask) - 1;
            if ((uint)slot >= (uint)_voices.Length) return false;

            var candidate = _voices[slot];
            if (candidate == null || candidate.BoundHandle != handle) return false;

            voice = candidate;
            return true;
        }

        /// <summary>句柄是否仍注册在案。</summary>
        public bool IsRegistered(ulong handle) => TryGet(handle, out _);

        /// <summary>
        /// 注销句柄：脱离用户 ID 索引、清空槽位并把句柄从声部侧抹掉。
        /// </summary>
        /// <returns>句柄此前是否注册在案。</returns>
        public bool Release(ulong handle, out TVoice voice)
        {
            if (!TryGet(handle, out voice)) return false;

            int slot = voice.VoiceSlot;
            UnlinkFromUserIndex(voice.UserId, slot);

            voice.BoundHandle = 0UL;
            voice.VoiceSlot = NoSlot;
            _voices[slot] = null;
            _userNext[slot] = NoSlot;
            _hashNext[slot] = NoSlot;
            PushFreeSlot(slot);
            _count--;
            return true;
        }

        /// <summary>
        /// 对注册在指定用户 ID 下的每个句柄执行操作。
        /// <para>走侵入式同 ID 链：先摘 next 再回调，所以回调里 Release 当前句柄不会跳过后续元素，
        /// 也不再需要复制一份快照列表。</para>
        /// </summary>
        public void ForEachHandleByUser(int userId, Action<ulong> action)
        {
            if (action == null || _buckets.Length == 0) return;

            for (int slot = _buckets[BucketOf(userId)]; slot != NoSlot;)
            {
                int next = _userNext[slot];   // 先摘：回调里解绑本条会改写 _userNext
                var voice = _voices[slot];
                if (voice != null && voice.UserId == userId && voice.BoundHandle != 0UL)
                {
                    action(voice.BoundHandle);
                }

                slot = next;
            }
        }

        /// <summary>清空全部绑定：声部侧句柄一并归零，避免残留把下一次 Bind 的旧句柄当成有效。</summary>
        public void Clear()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                var voice = _voices[i];
                if (voice == null) continue;
                voice.BoundHandle = 0UL;
                voice.VoiceSlot = NoSlot;
                _voices[i] = null;
                _userNext[i] = NoSlot;
                _hashNext[i] = NoSlot;
            }

            if (_buckets.Length > 0) Array.Fill(_buckets, NoSlot);
            _freeCount = 0;
            _count = 0;
            _generation = 0;
            // 容量留在原地：句柄表的大小反映峰值并发，缩回去只会在下一轮播放中途再扩一次
        }

        private void PushFreeSlot(int slot) => _freeSlots[_freeCount++] = slot;

        private int AcquireSlot()
        {
            if (_freeCount > 0) return _freeSlots[--_freeCount];
            if (_voices.Length >= SlotMask) return NoSlot;   // 20 位槽位用尽：绑不动，播放按失败处理

            GrowTo(_voices.Length == 0 ? InitialCapacity : _voices.Length << 1);
            return _freeSlots[--_freeCount];
        }

        /// <summary>
        /// 扩容到 <paramref name="capacity"/>（只增不减，桶数组取其 2 倍并取 2 的幂）。
        /// <para>现存条目原地搬走：声部上的 <c>VoiceSlot</c> 必须跟着改，否则老句柄会指到别人身上；
        /// 桶与同 ID 两条链都按新下标重建。</para>
        /// </summary>
        private void GrowTo(int capacity)
        {
            if (capacity > SlotMask) capacity = SlotMask;

            int oldLength = _voices.Length;
            Array.Resize(ref _voices, capacity);
            Array.Resize(ref _userNext, capacity);
            Array.Resize(ref _hashNext, capacity);
            Array.Resize(ref _freeSlots, capacity);

            int bucketCount = NextPowerOfTwo(Math.Max(capacity << 1, 16));
            _buckets = new int[bucketCount];
            Array.Fill(_buckets, NoSlot);
            _bucketMask = bucketCount - 1;

            for (int i = oldLength; i < capacity; i++)
            {
                _userNext[i] = NoSlot;
                _hashNext[i] = NoSlot;
            }

            // 自由表重铺：低位先出，稳态下槽位分配可复现
            _freeCount = 0;
            for (int i = capacity - 1; i >= 0; i--)
            {
                if (_voices[i] == null) _freeSlots[_freeCount++] = i;
            }

            // 在册声部凭 BoundHandle 判出，桶链与同 ID 链按新下标一并重挂
            for (int i = 0; i < capacity; i++)
            {
                var voice = _voices[i];
                if (voice == null || voice.BoundHandle == 0UL) continue;
                voice.VoiceSlot = i;
                int bucket = BucketOf(voice.UserId);
                _userNext[i] = NoSlot;
                _hashNext[i] = _buckets[bucket];
                _buckets[bucket] = i;
            }
        }

        private void UnlinkFromUserIndex(int userId, int slot)
        {
            int bucket = BucketOf(userId);
            int previous = NoSlot;
            for (int walk = _buckets[bucket]; walk != NoSlot; walk = _hashNext[walk])
            {
                if (walk == slot)
                {
                    if (previous < 0) _buckets[bucket] = _hashNext[slot];
                    else _hashNext[previous] = _hashNext[slot];

                    _hashNext[slot] = NoSlot;
                    return;
                }

                previous = walk;
            }
        }

        private int BucketOf(int userId) => (int)((uint)userId * 2654435761U) & _bucketMask;

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

        /// <summary>foreach 用的结构体枚举器：按槽位下标走，不经过任何接口分派。</summary>
        public struct SlotEnumerable
        {
            private readonly AudioHandleRegistry<TVoice> _registry;

            public SlotEnumerable(AudioHandleRegistry<TVoice> registry) => _registry = registry;

            public Enumerator GetEnumerator() => new Enumerator(_registry);
        }

        /// <summary>枚举出的槽位：句柄 + 声部。</summary>
        public struct Slot
        {
            public ulong Handle;
            public TVoice Voice;
        }

        /// <summary>结构体枚举器（零装箱、零分配）。</summary>
        public struct Enumerator
        {
            private readonly AudioHandleRegistry<TVoice> _registry;
            private int _index;

            public Enumerator(AudioHandleRegistry<TVoice> registry)
            {
                _registry = registry;
                _index = -1;
                Current = default;
            }

            public Slot Current { get; private set; }

            public bool MoveNext()
            {
                var voices = _registry?._voices;
                if (voices == null) return false;

                while (++_index < voices.Length)
                {
                    var voice = voices[_index];
                    if (voice != null && voice.BoundHandle != 0UL)
                    {
                        Current = new Slot { Handle = voice.BoundHandle, Voice = voice };
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
