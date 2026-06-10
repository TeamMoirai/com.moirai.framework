using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 运行时Id。
    /// <remarks>提供动态分配的运行时Id。</remarks>
    /// <example> public static readonly int ExampleEventId = RuntimeId.ToRuntimeId("ExampleEvent.ExampleEventId"); </example>
    /// </summary>
    public static class RuntimeId
    {
        /// <summary>
        /// Key->字符串 | Value->RuntimeIdTable)
        /// </summary>
        private static readonly Dictionary<string, int> _string2RuntimeMap = new Dictionary<string, int>();
        
        /// <summary>
        /// Key->RuntimeId    | Value->字符串 (Table)
        /// </summary>
        private static readonly Dictionary<int, string> _runtimeToStringMap = new Dictionary<int, string>();
        
        /// <summary>
        /// 当前运行时 Id。
        /// </summary>
        private static int _currentRuntimeId = 0;

        /// <summary>
        /// 字符串转 RuntimeId。
        /// </summary>
        /// <param name="value">字符串 Value。</param>
        /// <returns>RuntimeId。</returns>
        public static int ToRuntimeId(string value)
        {
            if (_string2RuntimeMap.TryGetValue(value, out var runtimeId))
            {
                return runtimeId;
            }

            runtimeId = ++_currentRuntimeId;
            _string2RuntimeMap[value] = runtimeId;
            _runtimeToStringMap[runtimeId] = value;

            return runtimeId;
        }

        /// <summary>
        /// RuntimeId 转字符串。
        /// </summary>
        /// <param name="runtimeId">RuntimeId。</param>
        /// <returns>字符串。</returns>
        public static string ToString(int runtimeId)
        {
            return _runtimeToStringMap.TryGetValue(runtimeId, out var value) ? value : string.Empty;
        }
    }
}