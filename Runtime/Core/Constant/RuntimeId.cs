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
        private static readonly Dictionary<string, int> s_String2RuntimeMap = new Dictionary<string, int>();

        /// <summary>
        /// Key->RuntimeId    | Value->字符串 (Table)
        /// </summary>
        private static readonly Dictionary<int, string> s_RuntimeToStringMap = new Dictionary<int, string>();

        /// <summary>
        /// 当前运行时 Id。
        /// </summary>
        private static int s_CurrentRuntimeId = 0;

        /// <summary>
        /// 字符串转 RuntimeId。
        /// </summary>
        /// <param name="value">字符串 Value。</param>
        /// <returns>RuntimeId。</returns>
        public static int ToRuntimeId(string value)
        {
            if (s_String2RuntimeMap.TryGetValue(value, out var runtimeId))
            {
                return runtimeId;
            }

            runtimeId = ++s_CurrentRuntimeId;
            s_String2RuntimeMap[value] = runtimeId;
            s_RuntimeToStringMap[runtimeId] = value;

            return runtimeId;
        }

        /// <summary>
        /// RuntimeId 转字符串。
        /// </summary>
        /// <param name="runtimeId">RuntimeId。</param>
        /// <returns>字符串。</returns>
        public static string ToString(int runtimeId)
        {
            return s_RuntimeToStringMap.TryGetValue(runtimeId, out var value) ? value : string.Empty;
        }
    }
}