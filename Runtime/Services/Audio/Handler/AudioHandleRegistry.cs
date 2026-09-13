using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 句柄注册表的声部引用契约——提供用户定义 ID 用于句柄反查与按 ID 批量操作。
    /// </summary>
    internal interface IAudioVoiceRef
    {
        /// <summary>用户定义 ID。</summary>
        int UserId { get; }
    }

    /// <summary>
    /// 音频句柄注册表（Unity / 中间件后端共用）。
    /// <para>职责：服务句柄生成、句柄 → 声部映射、用户 ID → 句柄列表映射（1 对多）、列表对象池。</para>
    /// <para>零分配约定：按 ID 遍历使用池化快照，action 内 Stop/Release 改写源列表不会破坏迭代或跳过元素。</para>
    /// </summary>
    /// <typeparam name="TVoice">后端声部类型（Unity 为 <see cref="AudioAgent"/>，中间件为私有 Voice）。</typeparam>
    internal sealed class AudioHandleRegistry<TVoice> where TVoice : class, IAudioVoiceRef
    {
        // 服务句柄 → 声部
        private readonly Dictionary<ulong, TVoice> _handleMap = new Dictionary<ulong, TVoice>(64);
        // 用户 ID → 服务句柄列表
        private readonly Dictionary<int, List<ulong>> _userHandleMap = new Dictionary<int, List<ulong>>(16);
        // List<ulong> 对象池
        private readonly Stack<List<ulong>> _listPool = new Stack<List<ulong>>(4);
        // 服务句柄生成器
        private ulong _nextHandle = 1UL;

        /// <summary>当前注册的句柄数量（诊断用）。</summary>
        public int Count => _handleMap.Count;

        /// <summary>全部句柄映射（后端轮询用，struct 枚举零分配；请勿在遍历中增删）。</summary>
        public Dictionary<ulong, TVoice> Map => _handleMap;

        /// <summary>生成下一个服务句柄（跳过保留值 0）。</summary>
        public ulong NextHandle()
        {
            ulong handle = _nextHandle++;
            if (handle == 0UL) handle = _nextHandle++;
            return handle;
        }

        /// <summary>绑定句柄与声部（同句柄重复绑定视为替换）。</summary>
        public void Bind(ulong handle, TVoice voice) => _handleMap[handle] = voice;

        /// <summary>登记用户 ID → 句柄映射。</summary>
        public void RegisterUser(int userId, ulong handle)
        {
            if (!_userHandleMap.TryGetValue(userId, out var list))
            {
                list = AcquireList();
                _userHandleMap[userId] = list;
            }

            list.Add(handle);
        }

        /// <summary>查询句柄绑定的声部。</summary>
        public bool TryGet(ulong handle, out TVoice voice) => _handleMap.TryGetValue(handle, out voice);

        /// <summary>句柄是否仍注册在案。</summary>
        public bool IsRegistered(ulong handle) => _handleMap.ContainsKey(handle);

        /// <summary>
        /// 注销句柄：从句柄映射与用户映射中移除。
        /// </summary>
        /// <param name="handle">服务句柄。</param>
        /// <param name="voice">注销的声部（未注册时为 null）。</param>
        /// <returns>句柄此前是否注册在案。</returns>
        public bool Release(ulong handle, out TVoice voice)
        {
            if (!_handleMap.TryGetValue(handle, out voice)) return false;

            _handleMap.Remove(handle);
            if (_userHandleMap.TryGetValue(voice.UserId, out var list))
            {
                list.Remove(handle);
                if (list.Count == 0)
                {
                    _userHandleMap.Remove(voice.UserId);
                    ReleaseList(list);
                }
            }

            return true;
        }

        /// <summary>
        /// 对注册在指定用户 ID 下的每个句柄执行操作（池化快照，稳态零分配）。
        /// </summary>
        public void ForEachHandleByUser(int userId, Action<ulong> action)
        {
            if (action == null) return;
            if (!_userHandleMap.TryGetValue(userId, out var handles) || handles.Count == 0) return;

            // 快照迭代：action 可能触发 Release 改写源列表
            var snapshot = AcquireList();
            snapshot.AddRange(handles);
            try
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    action(snapshot[i]);
                }
            }
            finally
            {
                ReleaseList(snapshot);
            }
        }

        /// <summary>清空全部映射并回收列表。</summary>
        public void Clear()
        {
            _handleMap.Clear();
            foreach (var list in _userHandleMap.Values)
            {
                ReleaseList(list);
            }

            _userHandleMap.Clear();
        }

        private List<ulong> AcquireList() => _listPool.Count > 0 ? _listPool.Pop() : new List<ulong>(2);

        private void ReleaseList(List<ulong> list)
        {
            list.Clear();
            _listPool.Push(list);
        }
    }
}
