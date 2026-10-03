namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档序列化后端标识表：容器逐块记录的 2 字节线格式 ID 与保留区约定。
    /// </summary>
    /// <remarks>
    /// 数值即落盘字节（<see cref="SaveFileContainer"/> 每块在模式版本后写 2 字节小端后端 ID），一经发行不得重排——旧档按原数值还原。 <br />
    /// <c>0</c>~<see cref="RESERVED_MAX"/> 为框架保留区：区间内只承认下列内建标识，<see cref="SaveSerializerRegistry.Register"/> <br />
    /// 拒绝其余（<see cref="KEY_VALUE"/> 为组件捕获格式专用），项目自定义后端从 1000 起分配。 <br />
    /// 成员写成 <c>const ushort</c> 而非枚举：特性参数只接受编译期常量，枚举成员会把可扩展的 ID 集合封死。
    /// </remarks>
    public static class SaveBackendIds
    {
        /// <summary>框架内置 JSON 序列化（零分配字节通路，未配置时的回退后端）。</summary>
        public const ushort JSON = 0;

        /// <summary>MessagePack 二进制序列化（类型需 <c>MessagePackObject</c> 标注）。</summary>
        public const ushort MESSAGE_PACK = 1;

        /// <summary>MemoryPack 二进制序列化（类型需 <c>MemoryPackable</c> 标注 + SourceGenerator）。</summary>
        public const ushort MEMORY_PACK = 2;

        /// <summary>protobuf-net 二进制序列化（类型需 <c>ProtoContract</c> 标注）。</summary>
        public const ushort PROTOBUF = 3;

        /// <summary>框架内置键值捕获格式（无代码保存组件专用保留标识）。</summary>
        public const ushort KEY_VALUE = 254;

        /// <summary>框架保留区上界（含）：区间内除内建 ID 外一律不接受外部注册，项目自定义后端从 1000 起分配。</summary>
        public const ushort RESERVED_MAX = 255;

        /// <summary>
        /// 取后端标识的显示名：给人看的日志、检视器与调试面板统一走这里，不再直接印裸数字。
        /// </summary>
        /// <remarks>用字面名而非 <c>nameof(...)</c>：三个二进制实现受 <c>*_INSTALLED</c> 宏门控， <br />
        /// 未接入的工程里那些类型根本不存在，<c>nameof</c> 会直接把这一格编不过。</remarks>
        /// <param name="backendId">后端标识。</param>
        /// <returns>内建标识返回其名字；其余返回 <c>ID &lt;数值&gt;</c>（未注册的 ID 本就没有名字）。</returns>
        public static string DisplayName(ushort backendId)
        {
            switch (backendId)
            {
                case JSON: return "Json";
                case MESSAGE_PACK: return "MessagePack";
                case MEMORY_PACK: return "MemoryPack";
                case PROTOBUF: return "Protobuf";
                case KEY_VALUE: return "KeyValue";
                default: return StringUtility.Format("ID {0}", backendId);
            }
        }
    }
}
