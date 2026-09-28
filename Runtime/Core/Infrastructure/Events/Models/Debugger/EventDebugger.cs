using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Moirai.Atropos;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos.Events
{
    internal readonly struct EventDebuggerLogCall : IDisposable
    {
#if UNITY_EDITOR
        private readonly Delegate m_Callback;
        private readonly EventBase m_Event;
        private readonly long m_Start;
        private readonly bool m_IsPropagationStopped;
        private readonly bool m_IsImmediatePropagationStopped;
        private readonly bool m_IsDefaultPrevented;
#endif
        public EventDebuggerLogCall(Delegate callback, EventBase evt)
        {
#if UNITY_EDITOR
            m_Callback = callback;
            m_Event = evt;

            m_Start = (long)(Time.realtimeSinceStartup * 1000.0f);
            m_IsPropagationStopped = evt.IsPropagationStopped;
            m_IsImmediatePropagationStopped = evt.IsImmediatePropagationStopped;
            m_IsDefaultPrevented = evt.IsDefaultPrevented;
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            if (m_Event != null && m_Event.Log)
            {
                m_Event.EventLogger.LogCall(GetCallbackHashCode(), GetCallbackName(), m_Event,
                    m_IsPropagationStopped != m_Event.IsPropagationStopped,
                    m_IsImmediatePropagationStopped != m_Event.IsImmediatePropagationStopped,
                    m_IsDefaultPrevented != m_Event.IsDefaultPrevented,
                    (long)(Time.realtimeSinceStartup * 1000.0f) - m_Start);
            }
#endif
        }

#if UNITY_EDITOR
        private string GetCallbackName()
        {
            if (m_Callback == null)
            {
                return "No callback";
            }

            if (m_Callback.Target != null)
            {
                return m_Callback.Target.GetType().FullName + "." + m_Callback.Method.Name;
            }

            if (m_Callback.Method.DeclaringType != null)
            {
                return m_Callback.Method.DeclaringType.FullName + "." + m_Callback.Method.Name;
            }

            return m_Callback.Method.Name;
        }

        private int GetCallbackHashCode()
        {
            return m_Callback?.GetHashCode() ?? 0;
        }

#endif
    }


    internal readonly struct EventDebuggerLogExecuteDefaultAction : IDisposable
    {
#if UNITY_EDITOR
        private readonly EventBase m_Event;
        private readonly long m_Start;
#endif
        public EventDebuggerLogExecuteDefaultAction(EventBase evt)
        {
#if UNITY_EDITOR
            m_Event = evt;
            m_Start = (long)(Time.realtimeSinceStartup * 1000.0f);
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            if (m_Event != null && m_Event.Log)
            {
                m_Event.EventLogger.LogExecuteDefaultAction(m_Event, m_Event.PropagationPhase,
                    (long)(Time.realtimeSinceStartup * 1000.0f) - m_Start);
            }
#endif
        }
    }

    internal class EventDebugger
    {
        /// <summary>
        /// 获取或设置调试器关联的事件协调器；编辑器构建下 getter 返回 <see cref="CoordinatorDebug"/>，setter 为空操作。
        /// </summary>
        public IEventCoordinator Coordinator
        {
#if UNITY_EDITOR
            get { return CoordinatorDebug; }
            set
            {
                /* 编辑器下忽略赋值 */
            }
#else
            get; set;
#endif
        }

#if UNITY_EDITOR
        private IEventCoordinator m_CoordinatorDebug;
        /// <summary>
        /// 获取或设置编辑器调试使用的事件协调器，设置时会注册其事件类型处理计数。
        /// </summary>
        public IEventCoordinator CoordinatorDebug
        {
            get { return m_CoordinatorDebug; }
            set
            {
                m_CoordinatorDebug = value;
                if (m_CoordinatorDebug != null)
                {
                    if (!m_EventTypeProcessedCount.ContainsKey(Coordinator))
                        m_EventTypeProcessedCount.Add(Coordinator, new Dictionary<long, int>());
                }
            }
        }
#endif

        /// <summary>
        /// 获取或设置当前是否正在回放事件。
        /// </summary>
        public bool IsReplaying { get; internal set; }
        /// <summary>
        /// 获取或设置回放速度倍率（默认 <c>1.0</c>）。
        /// </summary>
        public float PlaybackSpeed { get; set; } = 1.0f;
        /// <summary>
        /// 获取或设置回放是否处于暂停状态。
        /// </summary>
        public bool IsPlaybackPaused { get; set; }

        /// <summary>
        /// 将当前协调器的修改计数加一，用于外部检测调试数据是否变化。
        /// </summary>
        public void UpdateModificationCount()
        {
            if (Coordinator == null)
                return;

            if (!m_ModificationCount.TryGetValue(Coordinator, out var count))
            {
                count = 0;
            }

            count++;
            m_ModificationCount[Coordinator] = count;
        }

        /// <summary>
        /// 记录事件处理开始，并更新修改计数。
        /// </summary>
        /// <param name="evt">正在处理的事件。</param>
        public void BeginProcessEvent(EventBase evt)
        {
            AddBeginProcessEvent(evt);
            UpdateModificationCount();
        }

        /// <summary>
        /// 记录事件处理结束及其耗时，并更新修改计数。
        /// </summary>
        /// <param name="evt">处理完成的事件。</param>
        /// <param name="duration">事件处理耗时（毫秒）。</param>
        public void EndProcessEvent(EventBase evt, long duration)
        {
            AddEndProcessEvent(evt, duration);
            UpdateModificationCount();
        }

        /// <summary>
        /// 记录一次事件回调调用，并更新修改计数。
        /// </summary>
        /// <param name="cbHashCode">回调的哈希码。</param>
        /// <param name="cbName">回调的显示名称。</param>
        /// <param name="evt">回调关联的事件。</param>
        /// <param name="propagationHasStopped">回调执行后传播是否已停止。</param>
        /// <param name="immediatePropagationHasStopped">回调执行后是否已立即停止同元素上的后续回调。</param>
        /// <param name="defaultHasBeenPrevented">回调执行后是否已阻止默认行为。</param>
        /// <param name="duration">回调耗时（毫秒）。</param>
        public void LogCall(int cbHashCode, string cbName, EventBase evt, bool propagationHasStopped, bool immediatePropagationHasStopped, bool defaultHasBeenPrevented, long duration)
        {
            AddCallObject(cbHashCode, cbName, evt, propagationHasStopped, immediatePropagationHasStopped, defaultHasBeenPrevented, duration);
            UpdateModificationCount();
        }

        /// <summary>
        /// 记录一次默认行为执行，并更新修改计数。
        /// </summary>
        /// <param name="evt">触发默认行为的事件。</param>
        /// <param name="phase">执行默认行为时所处的传播阶段。</param>
        /// <param name="duration">默认行为执行耗时（毫秒）。</param>
        public void LogExecuteDefaultAction(EventBase evt, PropagationPhase phase, long duration)
        {
            AddExecuteDefaultAction(evt, phase, duration);
            UpdateModificationCount();
        }
        /// <summary>
        /// 记录事件的传播路径（仅编辑器构建下生效）。
        /// </summary>
        /// <param name="evt">要记录的事件。</param>
        /// <param name="paths">事件的传播路径。</param>
        public static void LogPropagationPaths(EventBase evt, PropagationPaths paths)
        {
#if UNITY_EDITOR
            if (evt.Log)
            {
                evt.EventLogger.LogPropagationPathsInternal(evt, paths);
            }
#endif
        }
        /// <summary>
        /// 记录事件传播路径的副本，并更新修改计数。
        /// </summary>
        /// <param name="evt">要记录的事件。</param>
        /// <param name="paths">事件的传播路径。</param>
        public void LogPropagationPathsInternal(EventBase evt, PropagationPaths paths)
        {
            var pathsCopy = paths == null ? new PropagationPaths() : new PropagationPaths(paths);
            AddPropagationPaths(evt, pathsCopy);
            UpdateModificationCount();
        }
        /// <summary>
        /// 将事件的传播路径添加到日志记录；调试器挂起时忽略。
        /// </summary>
        /// <param name="evt">要记录的事件。</param>
        /// <param name="paths">事件的传播路径。</param>
        public void AddPropagationPaths(EventBase evt, PropagationPaths paths)
        {
            if (Suspended)
                return;

            if (m_Log)
            {
                var pathObject = new EventDebuggerPathTrace(Coordinator, evt, paths);

                if (!m_EventPathObjects.TryGetValue(Coordinator, out var list))
                {
                    list = new List<EventDebuggerPathTrace>();
                    m_EventPathObjects.Add(Coordinator, list);
                }

                list.Add(pathObject);
            }
        }
        /// <summary>
        /// 获取指定协调器的事件回调调用记录，可按事件记录筛选。
        /// </summary>
        /// <param name="coordinator">要查询的事件协调器。</param>
        /// <param name="evt">筛选依据的事件记录，为 null 时返回全部记录。</param>
        /// <returns>回调调用记录列表；无记录时返回 null。</returns>
        public List<EventDebuggerCallTrace> GetCalls(IEventCoordinator coordinator, EventDebuggerEventRecord evt = null)
        {
            if (!m_EventCalledObjects.TryGetValue(coordinator, out var list))
            {
                return null;
            }

            if ((evt != null) && (list != null))
            {
                List<EventDebuggerCallTrace> filteredList = new List<EventDebuggerCallTrace>();
                foreach (var callObject in list)
                {
                    if (callObject.EventBase.EventId == evt.EventId)
                    {
                        filteredList.Add(callObject);
                    }
                }

                list = filteredList;
            }

            return list;
        }
        /// <summary>
        /// 获取指定协调器的事件传播路径记录，可按事件记录筛选。
        /// </summary>
        /// <param name="coordinator">要查询的事件协调器。</param>
        /// <param name="evt">筛选依据的事件记录，为 null 时返回全部记录。</param>
        /// <returns>传播路径记录列表；无记录时返回 null。</returns>
        public List<EventDebuggerPathTrace> GetPropagationPaths(IEventCoordinator coordinator, EventDebuggerEventRecord evt = null)
        {
            if (!m_EventPathObjects.TryGetValue(coordinator, out var list))
            {
                return null;
            }

            if ((evt != null) && (list != null))
            {
                List<EventDebuggerPathTrace> filteredList = new List<EventDebuggerPathTrace>();
                foreach (var pathObject in list)
                {
                    if (pathObject.EventBase.EventId == evt.EventId)
                    {
                        filteredList.Add(pathObject);
                    }
                }

                list = filteredList;
            }

            return list;
        }
        /// <summary>
        /// 获取指定协调器的默认行为执行记录，可按事件记录筛选。
        /// </summary>
        /// <param name="coordinator">要查询的事件协调器。</param>
        /// <param name="evt">筛选依据的事件记录，为 null 时返回全部记录。</param>
        /// <returns>默认行为执行记录列表；无记录时返回 null。</returns>
        public List<EventDebuggerDefaultActionTrace> GetDefaultActions(IEventCoordinator coordinator, EventDebuggerEventRecord evt = null)
        {
            if (!m_EventDefaultActionObjects.TryGetValue(coordinator, out var list))
            {
                return null;
            }

            if ((evt != null) && (list != null))
            {
                List<EventDebuggerDefaultActionTrace> filteredList = new List<EventDebuggerDefaultActionTrace>();
                foreach (var defaultActionObject in list)
                {
                    if (defaultActionObject.EventBase.EventId == evt.EventId)
                    {
                        filteredList.Add(defaultActionObject);
                    }
                }

                list = filteredList;
            }

            return list;
        }


        /// <summary>
        /// 获取指定协调器的事件处理开始/结束记录，可按事件记录筛选。
        /// </summary>
        /// <param name="coordinator">要查询的事件协调器。</param>
        /// <param name="evt">筛选依据的事件记录，为 null 时返回全部记录。</param>
        /// <returns>事件处理记录列表；无记录时返回 null。</returns>
        public List<EventDebuggerTrace> GetBeginEndProcessedEvents(IEventCoordinator coordinator, EventDebuggerEventRecord evt = null)
        {
            if (!m_EventProcessedEvents.TryGetValue(coordinator, out var list))
            {
                return null;
            }

            if ((evt != null) && (list != null))
            {
                List<EventDebuggerTrace> filteredList = new List<EventDebuggerTrace>();
                foreach (var defaultActionObject in list)
                {
                    if (defaultActionObject.EventBase.EventId == evt.EventId)
                    {
                        filteredList.Add(defaultActionObject);
                    }
                }

                list = filteredList;
            }

            return list;
        }

        /// <summary>
        /// 获取指定协调器的修改计数；协调器无效或无记录时返回 -1。
        /// </summary>
        /// <param name="coordinator">要查询的事件协调器。</param>
        /// <returns>修改计数，无效时为 -1。</returns>
        public long GetModificationCount(IEventCoordinator coordinator)
        {
            if (coordinator == null)
                return -1;

            if (!m_ModificationCount.TryGetValue(coordinator, out var modificationCount))
            {
                modificationCount = -1;
            }

            return modificationCount;
        }

        /// <summary>
        /// 清除调试日志记录；协调器为 null 时清除全部记录，否则仅清除当前协调器的记录。
        /// </summary>
        public void ClearLogs()
        {
            UpdateModificationCount();

            if (Coordinator == null)
            {
                m_EventCalledObjects.Clear();
                m_EventDefaultActionObjects.Clear();
                m_EventProcessedEvents.Clear();
                m_StackOfProcessedEvent.Clear();
                m_EventTypeProcessedCount.Clear();
                return;
            }

            m_EventCalledObjects.Remove(Coordinator);
            m_EventDefaultActionObjects.Remove(Coordinator);
            m_EventProcessedEvents.Remove(Coordinator);
            m_StackOfProcessedEvent.Remove(Coordinator);

            if (m_EventTypeProcessedCount.TryGetValue(Coordinator, out var eventTypeProcessedForCoordinator))
                eventTypeProcessedForCoordinator.Clear();
        }

        /// <summary>
        /// 将选定的事件记录列表以 JSON 形式保存为回放会话文件。
        /// </summary>
        /// <param name="path">保存文件的完整路径。</param>
        /// <param name="eventList">要保存的事件记录列表。</param>
        public void SaveReplaySessionFromSelection(string path, List<EventDebuggerEventRecord> eventList)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var recordSave = new EventDebuggerRecordList() { eventList = eventList };
            var json = JsonUtility.ToJson(recordSave);
            File.WriteAllText(path, json);
            LogUtility.Info($"Saved under: {path}");
        }

        /// <summary>
        /// 从文件加载回放会话的事件记录列表。
        /// </summary>
        /// <param name="path">回放会话文件的完整路径。</param>
        /// <returns>加载的事件记录列表；路径无效时返回 null。</returns>
        public EventDebuggerRecordList LoadReplaySession(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var fileContent = File.ReadAllText(path);
            return UnityEngine.JsonUtility.FromJson<EventDebuggerRecordList>(fileContent);
        }

        /// <summary>
        /// 按时间戳顺序回放事件记录（协程），事件间隔依据回放速度缩放，回放中每帧让步一次。
        /// </summary>
        /// <param name="eventBases">要回放的事件记录集合。</param>
        /// <param name="refreshList">回放进度刷新回调，参数为当前索引与事件总数。</param>
        /// <returns>回放协程的迭代器。</returns>
        public IEnumerator ReplayEvents(IEnumerable<EventDebuggerEventRecord> eventBases, Action<int, int> refreshList)
        {
            if (eventBases == null)
                yield break;

            IsReplaying = true;
            var doReplay = DoReplayEvents(eventBases, refreshList);
            while (doReplay.MoveNext())
            {
                yield return null;
            }
        }

        private IEnumerator DoReplayEvents(IEnumerable<EventDebuggerEventRecord> eventBases, Action<int, int> refreshList)
        {
            var sortedEvents = eventBases.OrderBy(e => e.Timestamp).ToList();
            var sortedEventsCount = sortedEvents.Count;

            IEnumerator AwaitForNextEvent(int currentIndex)
            {
                if (currentIndex == sortedEvents.Count - 1)
                    yield break;

                var deltaTimestampMs = sortedEvents[currentIndex + 1].Timestamp - sortedEvents[currentIndex].Timestamp;

                var timeMs = 0.0f;
                while (timeMs < deltaTimestampMs)
                {
                    if (IsPlaybackPaused)
                    {
                        yield return null;
                    }
                    else
                    {
                        var time = EventBase.TimeSinceStartupMs();
                        yield return null;
                        var delta = EventBase.TimeSinceStartupMs() - time;
                        timeMs += delta * PlaybackSpeed;
                    }
                }
            }

            for (var i = 0; i < sortedEventsCount; i++)
            {
                if (!IsReplaying)
                    break;

                var eventBase = sortedEvents[i];
                EventBase newEvent = null;
                try
                {
                    Type eventType = Type.GetType(eventBase.EventType);
                    var getPooledMethod = eventType.GetStaticMethodWithNoParametersInBase("GetPooled");
                    Assert.IsTrue(getPooledMethod != null);
                    newEvent = (EventBase)getPooledMethod.Invoke(null, null);
                    JsonConvert.PopulateObject(eventBase.JsonData, newEvent);
                }
                catch (Exception ex)
                {
                    LogUtility.Error($"[EventDebugger] Failed to reconstruct event '{eventBase.EventBaseName}' (type: {eventBase.EventType}): {ex}");
                }
                if (newEvent == null)
                {
                    LogUtility.Info("Skipped event (" + eventBase.EventBaseName + "): " + eventBase);
                    var awaitSkipped = AwaitForNextEvent(i);
                    while (awaitSkipped.MoveNext()) yield return null;
                    continue;
                }
                eventBase.Target.SendEvent(newEvent);
                newEvent.Dispose();
                refreshList?.Invoke(i, sortedEventsCount);
                LogUtility.Info($"Replayed event {eventBase.EventId} ({eventBase.EventBaseName}): {newEvent}");
                var await = AwaitForNextEvent(i);
                while (await.MoveNext()) yield return null;
            }

            IsReplaying = false;
        }

        /// <summary>
        /// 停止回放并取消暂停状态。
        /// </summary>
        public void StopPlayback()
        {
            IsReplaying = false;
            IsPlaybackPaused = false;
        }

        internal struct HistogramRecord
        {
            public long count;
            public long duration;
        }

        /// <summary>
        /// 按事件名称统计事件数量与总耗时，构建执行直方图。
        /// </summary>
        /// <param name="eventBases">参与统计的事件记录列表，为 null 或空时统计全部记录。</param>
        /// <returns>事件名称到数量/耗时统计的字典；无可用记录时返回 null。</returns>
        public Dictionary<string, HistogramRecord> ComputeHistogram(List<EventDebuggerEventRecord> eventBases)
        {
            if (Coordinator == null || !m_EventProcessedEvents.TryGetValue(Coordinator, out var list))
                return null;

            if (list == null)
                return null;

            Dictionary<string, HistogramRecord> histogram = new Dictionary<string, HistogramRecord>();
            foreach (var callObject in list)
            {
                if (eventBases == null || eventBases.Count == 0 || eventBases.Contains(callObject.EventBase))
                {
                    var key = callObject.EventBase.EventBaseName;
                    var totalDuration = callObject.Duration;
                    long totalCount = 1;
                    if (histogram.TryGetValue(key, out var currentHistogramRecord))
                    {
                        totalDuration += currentHistogramRecord.duration;
                        totalCount += currentHistogramRecord.count;
                    }

                    histogram[key] = new HistogramRecord { count = totalCount, duration = totalDuration };
                }
            }

            return histogram;
        }

        // 回调对象记录
        private readonly Dictionary<IEventCoordinator, List<EventDebuggerCallTrace>> m_EventCalledObjects;
        private readonly Dictionary<IEventCoordinator, List<EventDebuggerDefaultActionTrace>> m_EventDefaultActionObjects;
        private readonly Dictionary<IEventCoordinator, List<EventDebuggerPathTrace>> m_EventPathObjects;
        private readonly Dictionary<IEventCoordinator, List<EventDebuggerTrace>> m_EventProcessedEvents;
        private readonly Dictionary<IEventCoordinator, Stack<EventDebuggerTrace>> m_StackOfProcessedEvent;
        private readonly Dictionary<IEventCoordinator, Dictionary<long, int>> m_EventTypeProcessedCount;

        /// <summary>
        /// 获取当前协调器按事件类型 ID 统计的处理数量；无记录时返回 null。
        /// </summary>
        public Dictionary<long, int> EventTypeProcessedCount => m_EventTypeProcessedCount.TryGetValue(Coordinator, out var eventTypeProcessedCountForCoordinator) ? eventTypeProcessedCountForCoordinator : null;

        private readonly Dictionary<IEventCoordinator, long> m_ModificationCount;
        private readonly bool m_Log;

        /// <summary>
        /// 获取或设置是否挂起日志记录，挂起期间不再添加新的调试记录。
        /// </summary>
        public bool Suspended { get; set; }

        // 方法
        /// <summary>
        /// 初始化事件调试器实例。
        /// </summary>
        public EventDebugger()
        {
            m_EventCalledObjects = new Dictionary<IEventCoordinator, List<EventDebuggerCallTrace>>();
            m_EventDefaultActionObjects = new Dictionary<IEventCoordinator, List<EventDebuggerDefaultActionTrace>>();
            m_StackOfProcessedEvent = new Dictionary<IEventCoordinator, Stack<EventDebuggerTrace>>();
            m_EventProcessedEvents = new Dictionary<IEventCoordinator, List<EventDebuggerTrace>>();
            m_EventTypeProcessedCount = new Dictionary<IEventCoordinator, Dictionary<long, int>>();
            m_ModificationCount = new Dictionary<IEventCoordinator, long>();
            m_EventPathObjects = new Dictionary<IEventCoordinator, List<EventDebuggerPathTrace>>();
            m_Log = true;
        }

        private void AddCallObject(int cbHashCode, string cbName, EventBase evt, bool propagationHasStopped, bool immediatePropagationHasStopped, bool defaultHasBeenPrevented, long duration)
        {
            if (Suspended)
                return;

            if (m_Log)
            {
                var callObject = new EventDebuggerCallTrace(Coordinator, evt, cbHashCode, cbName, propagationHasStopped, immediatePropagationHasStopped, defaultHasBeenPrevented, duration);

                if (!m_EventCalledObjects.TryGetValue(Coordinator, out var list))
                {
                    list = new List<EventDebuggerCallTrace>();
                    m_EventCalledObjects.Add(Coordinator, list);
                }

                list.Add(callObject);
            }
        }

        private void AddExecuteDefaultAction(EventBase evt, PropagationPhase phase, long duration)
        {
            if (Suspended)
                return;

            if (m_Log)
            {
                var defaultActionObject = new EventDebuggerDefaultActionTrace(Coordinator, evt, phase, duration);

                if (!m_EventDefaultActionObjects.TryGetValue(Coordinator, out var list))
                {
                    list = new List<EventDebuggerDefaultActionTrace>();
                    m_EventDefaultActionObjects.Add(Coordinator, list);
                }

                list.Add(defaultActionObject);
            }
        }



        private void AddBeginProcessEvent(EventBase evt)
        {
            if (Suspended)
                return;

            var dbgObject = new EventDebuggerTrace(Coordinator, evt, -1);

            if (!m_StackOfProcessedEvent.TryGetValue(Coordinator, out var stack))
            {
                stack = new Stack<EventDebuggerTrace>();
                m_StackOfProcessedEvent.Add(Coordinator, stack);
            }

            if (!m_EventProcessedEvents.TryGetValue(Coordinator, out var list))
            {
                list = new List<EventDebuggerTrace>();
                m_EventProcessedEvents.Add(Coordinator, list);
            }

            list.Add(dbgObject);
            stack.Push(dbgObject);

            if (!m_EventTypeProcessedCount.TryGetValue(Coordinator, out var eventTypeProcessedCountForCoordinator))
                return;

            if (!eventTypeProcessedCountForCoordinator.TryGetValue(dbgObject.EventBase.EventTypeId, out var count))
                count = 0;

            eventTypeProcessedCountForCoordinator[dbgObject.EventBase.EventTypeId] = count + 1;
        }

        private void AddEndProcessEvent(EventBase evt, long duration)
        {
            if (Suspended)
                return;

            bool evtHandled = false;
            if (m_StackOfProcessedEvent.TryGetValue(Coordinator, out var stack))
            {
                if (stack.Count > 0)
                {
                    var dbgObject = stack.Peek();
                    if (dbgObject.EventBase.EventId == evt.EventId)
                    {
                        stack.Pop();
                        dbgObject.Duration = duration;

                        // 若目标在 AddBeginProcessEvent 时未知，则在此更新。
                        if (dbgObject.EventBase.Target == null)
                        {
                            dbgObject.EventBase.Target = evt.Target;
                        }

                        evtHandled = true;
                    }
                }
            }

            if (!evtHandled)
            {
                var dbgObject = new EventDebuggerTrace(Coordinator, evt, duration);
                if (!m_EventProcessedEvents.TryGetValue(Coordinator, out var list))
                {
                    list = new List<EventDebuggerTrace>();
                    m_EventProcessedEvents.Add(Coordinator, list);
                }

                list.Add(dbgObject);

                if (!m_EventTypeProcessedCount.TryGetValue(Coordinator, out var eventTypeProcessedForCoordinator))
                    return;

                if (!eventTypeProcessedForCoordinator.TryGetValue(dbgObject.EventBase.EventTypeId, out var count))
                    count = 0;

                eventTypeProcessedForCoordinator[dbgObject.EventBase.EventTypeId] = count + 1;
            }
        }

        /// <summary>
        /// 获取对象的显示名称（类型名 + 对象名，可附加实例 ID）。
        /// </summary>
        /// <param name="obj">要显示的对象。</param>
        /// <param name="withHashCode">是否在名称后附加实例 ID（十六进制）。</param>
        /// <returns>对象的显示名称；对象为 null 时返回空字符串。</returns>
        public static string GetObjectDisplayName(object obj, bool withHashCode = true)
        {
            if (obj == null) return string.Empty;

            var type = obj.GetType();
            var objectName = GetTypeDisplayName(type);
            // 分两种情况处理
            // MonoBehaviour 实现了 IEventHandler
            if (obj is Behaviour behaviour)
            {
                objectName += "#" + behaviour.gameObject.name;
                if (withHashCode)
                {
                    //运行时优先使用 instanceID
                    objectName += " (" + UnityUtility.GetObjectEntityId(behaviour).ToString("x8") + ")";
                }
            }
            // 依附于 MonoBehaviour 的 EventHandler
            else if (obj is IBehaviourScope bs)
            {
                objectName += "#" + bs.Behaviour.gameObject.name;
                if (withHashCode)
                {
                    //运行时优先使用 instanceID
                    objectName += " (" + UnityUtility.GetObjectEntityId(bs.Behaviour).ToString("x8") + ")";
                }
            }

            else if (withHashCode)
            {
                objectName += " (" + obj.GetHashCode().ToString("x8") + ")";
            }

            return objectName;
        }

        /// <summary>
        /// 获取类型的显示名称，泛型类型输出为 <c>Name&lt;T&gt;</c> 形式。
        /// </summary>
        /// <param name="type">要显示的类型。</param>
        /// <returns>类型的显示名称。</returns>
        public static string GetTypeDisplayName(Type type)
        {
            return type.IsGenericType ? $"{type.Name.TrimEnd('`', '1')}<{type.GetGenericArguments()[0].Name}>" : type.Name;
        }
    }
}
