using System;

namespace Moirai.Atropos.Storage
{
    /// <summary>
    /// 启动阶段核对存储能力，缺失时阻止依赖它的功能启动。
    /// </summary>
    public static class StorageRequirements
    {
        /// <summary>
        /// 校验所需能力均已具备，否则抛出不支持异常。
        /// </summary>
        public static void Ensure(StorageCapabilities available, StorageCapabilities required,
            string feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            StorageCapabilities missing = required & ~available;
            if (missing == StorageCapabilities.None) return;
            throw new NotSupportedException(
                feature + " requires storage capabilities that are missing: " + missing);
        }
    }
}
