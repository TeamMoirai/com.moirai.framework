using System;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频句柄注册表的声部引用契约。
    /// <para><see cref="VoiceSlot"/> 由注册表单点写入（绑定占槽、卸绑置回 -1），
    /// 实现方只需保证它随声部的复用生命周期被正确复位。</para>
    /// </summary>
    internal interface IAudioVoiceRef
    {
        /// <summary>
        /// 用户定义 ID（声部自报值）。
        /// <para><b>注册表不以它建索引</b>：索引一律以 <see cref="RegisterUser"/> 显式传入的 ID 为准，
        /// 因为 Unity 侧 <c>AudioAgent.ID</c> 要到播放调用内部才赋值，而登记必须发生在播放之前。</para>
        /// </summary>
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
    /// <para>职责：句柄生成与解析、句柄 → 声部绑定、用户 ID → 句柄遍历、按 ID 批量操作。</para>
    /// <para>不变量：句柄 ↔ 声部为 1:1；<see cref="Bind"/> 重绑前会自动卸掉声部旧句柄；句柄低 20 位是槽号、
    /// 高位是代次，代次不符即判假——所以槽位复用给新声部后，旧句柄不会命中。</para>
    /// <para>零分配：声部表是"一只声部数组 + 一只同 ID 链数组 + 一只自由栈"，用户 ID 索引是一张开址头表
    /// （key → 该 ID 的链头槽号）。这里没有 <c>Dictionary</c>、没有 <c>List&lt;ulong&gt;</c> 池、也没有遍历快照——
    /// 每次播放、每次停播和每帧多处扫描都走这张表，托管字典的哈希与扩容尖峰会直接进播放帧。</para>
    /// </summary>
    /// <typeparam name="TVoice">后端声部类型（Unity 为 <see cref="AudioAgent"/>，中间件为私有 Voice）。</typeparam>
    internal sealed class AudioHandleRegistry<TVoice> where TVoice : class, IAudioVoiceRef
    {
        private const int SlotBits = 20;
        private const int SlotMask = (1 << SlotBits) - 1;
        private const int NoSlot = -1;
        private const int NoHead = -1;

        /// <summary>槽位未登记用户 ID 的记号。真拿 int.MinValue 当分层 ID 不在本表支持范围内。</summary>
        private const int NoUser = int.MinValue;
        private const int InitialCapacity = 32;
        private const int InitialUserHeads = 16;

        private TVoice[] _voices = Array.Empty<TVoice>();
        private int[] _userNext = Array.Empty<int>();     // 同一 UserId 的下一条槽（链头记在头表里）
        private int[] _slotUser = Array.Empty<int>();     // 该槽登记在哪个 UserId 下；NoUser 表示未入链
        private int[] _freeSlots = Array.Empty<int>();

        private int[] _headKeys = Array.Empty<int>();     // 用户 ID 索引：key
        private int[] _headSlots = Array.Empty<int>();    // 该 key 的链头槽号；-1 表示链已空（槽位留着复用）
        private byte[] _headUsed = Array.Empty<byte>();   // 0 = 空位；1 = 已被某个 key 占住（链空也不释放，免得做删除标记）
        private int _headCount;
        private int _headMask;

        private int _freeCount;
        private int _count;
        private uint _generation;

        /// <summary>当前注册的句柄数量。</summary>
        public int Count => _count;

        /// <summary>只读遍历入口（结构体枚举器，foreach 不装箱）；遍历时不得解绑，需解绑请先收集槽号。</summary>
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
            if (_generation == 0U) _generation = 1U;   // 0 代次会让句柄退化成"仅槽号"，与未绑定态难分，跳过

            ulong handle = ((ulong)_generation << SlotBits) | (uint)(slot + 1);
            _voices[slot] = voice;
            voice.VoiceSlot = slot;
            voice.BoundHandle = handle;
            _count++;
            return handle;
        }

        /// <summary>
        /// 把句柄登记到 <paramref name="userId"/> 的索引链下。
        /// </summary>
        /// <remarks>
        /// ID 由调用方显式给出而不是读 <see cref="IAudioVoiceRef.UserId"/>：Unity 侧的 <c>AudioAgent.ID</c>
        /// 要到 <c>PlayWithRequest</c> / <c>LoadWithOptions</c> 里才赋值，而登记必须发生在播放之前
        /// （同步失败要能当场摘掉），那时声部上的 ID 还是上一轮的。
        /// <para>只登记一次：重复调用会在自己那条链上自环，所以先看槽位是否已入链。</para>
        /// </remarks>
        /// <returns>成功入链返回 true；句柄无效、或该槽已登记过返回 false。</returns>
        public bool RegisterUser(ulong handle, int userId)
        {
            if (!TryGet(handle, out var voice)) return false;

            int slot = voice.VoiceSlot;
            if (slot < 0 || _slotUser[slot] != NoUser) return false;

            int head = FindHead(userId, true);
            if (head < 0) return false;

            _slotUser[slot] = userId;
            _userNext[slot] = _headSlots[head];
            _headSlots[head] = slot;
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
            UnlinkFromUserIndex(slot);

            voice.BoundHandle = 0UL;
            voice.VoiceSlot = NoSlot;
            _voices[slot] = null;
            _userNext[slot] = NoSlot;
            PushFreeSlot(slot);
            _count--;
            return true;
        }

        /// <summary>
        /// 对注册在指定用户 ID 下的每个句柄执行操作。
        /// <para>先摘 next 再回调：所以回调里 Release 当前句柄不会跳过后续元素，
        /// 也不需要像旧实现那样复制一份快照列表。</para>
        /// </summary>
        public void ForEachHandleByUser(int userId, Action<ulong> action)
        {
            if (action == null || _headKeys.Length == 0) return;

            int head = FindHead(userId, false);
            if (head < 0) return;

            for (int slot = _headSlots[head]; slot != NoSlot;)
            {
                int next = _userNext[slot];   // 先摘：回调里解绑本条会改写 _userNext[slot]
                var voice = _voices[slot];
                if (voice != null && voice.BoundHandle != 0UL && _slotUser[slot] == userId)
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
            }

            Array.Fill(_userNext, NoSlot);
            Array.Fill(_slotUser, NoUser);
            Array.Clear(_headKeys, 0, _headKeys.Length);
            Array.Clear(_headSlots, 0, _headSlots.Length);
            Array.Clear(_headUsed, 0, _headUsed.Length);
            _headCount = 0;
            _freeCount = 0;
            _count = 0;
            _generation = 0;
            // 容量留在原地：表的大小反映峰值并发，缩回去只会在下一轮播放中途再扩一次
        }

        private void PushFreeSlot(int slot) => _freeSlots[_freeCount++] = slot;

        private int AcquireSlot()
        {
            if (_freeCount > 0) return _freeSlots[--_freeCount];
            if (_voices.Length >= SlotMask) return NoSlot;   // 20 位槽号用尽：绑不动，播放按失败处理

            GrowTo(_voices.Length == 0 ? InitialCapacity : _voices.Length << 1);
            return _freeSlots[--_freeCount];
        }

        /// <summary>
        /// 扩容到 <paramref name="capacity"/>（只增不减）。
        /// <para>Resize 保持既有下标，所以声部上的 <c>VoiceSlot</c> 与用户 ID 链都不用重挂——
        /// 只有新槽位需要把 next 初始化，并把它们铺进自由栈。</para>
        /// </summary>
        private void GrowTo(int capacity)
        {
            if (capacity > SlotMask) capacity = SlotMask;

            int oldLength = _voices.Length;
            Array.Resize(ref _voices, capacity);
            Array.Resize(ref _userNext, capacity);
            Array.Resize(ref _slotUser, capacity);
            Array.Resize(ref _freeSlots, capacity);

            for (int i = oldLength; i < capacity; i++)
            {
                _userNext[i] = NoSlot;
                _slotUser[i] = NoUser;
                _freeSlots[_freeCount++] = i;
            }
        }

        /// <summary>按槽位上记着的 key 摘链——不能拿声部的 UserId 反推，那个值可能已经换过。</summary>
        private void UnlinkFromUserIndex(int slot)
        {
            int userId = _slotUser[slot];
            _slotUser[slot] = NoUser;
            if (userId == NoUser) return;

            int head = FindHead(userId, false);
            if (head < 0) return;

            int previous = NoSlot;
            for (int walk = _headSlots[head]; walk != NoSlot; walk = _userNext[walk])
            {
                if (walk == slot)
                {
                    if (previous < 0) _headSlots[head] = _userNext[slot];
                    else _userNext[previous] = _userNext[slot];

                    _userNext[slot] = NoSlot;
                    return;
                }

                previous = walk;
            }
        }

        /// <summary>
        /// 线性探测找 <paramref name="userId"/> 的头表位；<paramref name="forInsert"/> 为真时空位就地占下。
        /// <para>头位一旦占用就不因链空而释放（链头记 -1 即可），于是探测遇到空位就能直接终止，
        /// 不需要删除标记——这是"同一批 ID 反复起播/停播"这种用法下最省事且不分配的做法。</para>
        /// </summary>
        private int FindHead(int userId, bool forInsert)
        {
            if (_headKeys.Length == 0)
            {
                if (!forInsert) return NoHead;
                GrowHeads(InitialUserHeads);
            }

            int index = HashUser(userId) & _headMask;
            for (int steps = 0; steps < _headKeys.Length; steps++, index = (index + 1) & _headMask)
            {
                if (_headUsed[index] == 0)
                {
                    if (!forInsert) return NoHead;

                    _headUsed[index] = 1;
                    _headKeys[index] = userId;
                    _headSlots[index] = NoSlot;
                    _headCount++;
                    // 装到一半就扩表：链头位置不变，只把 key 重铺到新表
                    if (_headCount << 1 >= _headKeys.Length) GrowHeads(_headKeys.Length << 1);
                    return index;
                }

                if (_headKeys[index] == userId) return index;
            }

            if (!forInsert) return NoHead;

            GrowHeads(_headKeys.Length << 1);
            return FindHead(userId, true);
        }

        /// <summary>换一张更大的头表，把每个 key 的链头原样搬过去（槽位链本身不动）。</summary>
        private void GrowHeads(int size)
        {
            int[] oldKeys = _headKeys;
            int[] oldSlots = _headSlots;
            byte[] oldUsed = _headUsed;

            _headKeys = new int[size];
            _headSlots = new int[size];
            _headUsed = new byte[size];
            Array.Fill(_headSlots, NoSlot);
            _headMask = size - 1;
            _headCount = 0;

            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldUsed[i] == 0) continue;

                int index = HashUser(oldKeys[i]) & _headMask;
                while (_headUsed[index] != 0) index = (index + 1) & _headMask;

                _headUsed[index] = 1;
                _headKeys[index] = oldKeys[i];
                _headSlots[index] = oldSlots[i];
                _headCount++;
            }
        }

        private static int HashUser(int userId) => unchecked((int)((uint)userId * 2654435761U));

        /// <summary>foreach 用的结构体枚举器入口：按槽位下标走，不经过任何接口分派。</summary>
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
                var voices = _registry == null ? null : _registry._voices;
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
