using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Moirai.Atropos.Events
{
    [Serializable]
    internal class EventDebuggerRecordList
    {
        /// <summary>
        /// 回放会话包含的事件记录列表。
        /// </summary>
        public List<EventDebuggerEventRecord> eventList;
    }
    
    [Serializable]
    internal class EventDebuggerEventRecord
    {
        /// <summary>
        /// 获取事件的显示名称（含泛型参数的类型名）。
        /// </summary>
        [field: SerializeField]
        public string EventBaseName { get; private set; }
        
        /// <summary>
        /// 获取事件类型 ID。
        /// </summary>
        [field: SerializeField]
        public long EventTypeId { get; private set; }
        
        /// <summary>
        /// 获取事件类型的程序集限定名。
        /// </summary>
        [field: SerializeField]
        public string EventType { get; private set; }
        
        /// <summary>
        /// 获取事件实例 ID。
        /// </summary>
        [field: SerializeField]
        public ulong EventId { get; private set; }
        
        [field: SerializeField]
        internal long Timestamp { get; private set; }
        
        /// <summary>
        /// 获取或设置事件目标元素。
        /// </summary>
        public IEventHandler Target { get; set; }
        
        /// <summary>
        /// 获取事件记录时所处的传播阶段。
        /// </summary>
        public PropagationPhase PropagationPhase { get; private set; }
        
        /// <summary>
        /// 获取或设置事件数据的 JSON 序列化结果。
        /// </summary>
        public string JsonData { get; set; }
        
        /// <summary>
        /// 用指定事件填充各记录字段。
        /// </summary>
        /// <param name="evt">作为记录来源的事件。</param>
        public void Init(EventBase evt)
        {
            var type = evt.GetType();
            EventBaseName = EventDebugger.GetTypeDisplayName(type);
            EventType = type.AssemblyQualifiedName;
            EventTypeId = evt.EventTypeId;
            EventId = evt.EventId;
            Timestamp = evt.Timestamp;
            Target = evt.Target;
            PropagationPhase = evt.PropagationPhase;
            
            JsonData = JsonConvert.SerializeObject(evt, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new CustomContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.None,
                MaxDepth = 1,
            });
        }

        /// <summary>
        /// 基于指定事件创建事件记录实例。
        /// </summary>
        /// <param name="evt">作为记录来源的事件。</param>
        public EventDebuggerEventRecord(EventBase evt)
        {
            Init(evt);
        }

        /// <summary>
        /// 获取格式化为 <c>HH:mm:ss.ffffff</c> 的时间戳字符串。
        /// </summary>
        /// <returns>格式化后的时间戳字符串。</returns>
        public string TimestampString()
        {
            long ticks = (long)(Timestamp / 1000f * TimeSpan.TicksPerSecond);
            return new DateTime(ticks).ToString("HH:mm:ss.ffffff");
        }
    }
}