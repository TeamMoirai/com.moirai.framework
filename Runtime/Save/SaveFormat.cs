namespace Moirai.Atropos.Persistence
{
    public static class SaveFormat
    {
        public const ushort JSON = 0;
        public const ushort MESSAGE_PACK = 1;
        public const ushort MEMORY_PACK = 2;
        public const ushort PROTOBUF = 3;
        public const ushort KEY_VALUE = 254;
        public const ushort CUSTOM_START = 1000;
        public const int HeaderVersion = 2;
        public const int ContainerVersion = 2;
        public const int HeaderSize = 32;
    }
}
