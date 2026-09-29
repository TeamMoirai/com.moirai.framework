namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存储后端能力自描述（值类型，构造后不可变）。
    /// </summary>
    /// <remarks>
    /// 调用方按能力降级：不支持原子改名时写入需自行加锁节流；不支持真异步 IO 时异步 API 为线程池卸载语义；易失存储（如云缓存目录）的存档须自行镜像。
    /// 同步读权威性（<see cref="SyncReadsAuthoritative"/>）：云镜像后端的同步原语仅作用本地镜像——为 <c>false</c> 时同步裸名读 API 返回的是可能滞后的本地镜像， <br />
    /// 远端内容须经异步 API 族裁决；本地文件后端恒为 <c>true</c>。
    /// </remarks>
    public readonly struct SaveStorageCapabilities
    {
        /// <summary>
        /// 替换目标时不出现半写窗口，且中断后旧档必可恢复。
        /// </summary>
        /// <remarks>
        /// 判据不是「底层用过一次原子 rename」：平台无原子替换能力时，后端改走「旧档改名到日志位 → 新档改名到位 → 清日志」的中转写法，任一时刻要么新档完整、要么旧档完整留在日志位并由下次初始化抬回。代价是中转期间主档路径短暂缺席。
        /// </remarks>
        public readonly bool SupportsAtomicRename;

        /// <summary>
        /// 支持真异步 IO（否则异步 API 为线程池卸载同步原语）。
        /// </summary>
        public readonly bool SupportsTrueAsyncIO;

        /// <summary>
        /// 单档推荐最大字节数（-1 表示不设上限）。
        /// </summary>
        public readonly long MaxRecommendedSize;

        /// <summary>
        /// 易失存储（后端不保证持久化，重要存档需自行镜像）。
        /// </summary>
        public readonly bool VolatileStorage;

        /// <summary>
        /// 同步读权威（<c>true</c> = 同步裸名读 API 返回权威数据；<c>false</c> = 同步读仅见本地镜像，远端内容须经异步 API 族裁决）。
        /// </summary>
        public readonly bool SyncReadsAuthoritative;

        /// <summary>
        /// 创建能力描述。
        /// </summary>
        /// <param name="supportsAtomicRename">是否支持原子改名/替换。</param>
        /// <param name="supportsTrueAsyncIO">是否支持真异步 IO。</param>
        /// <param name="maxRecommendedSize">单档推荐最大字节数（-1 不设上限）。</param>
        /// <param name="volatileStorage">是否易失存储。</param>
        /// <param name="syncReadsAuthoritative">同步读是否权威（云镜像后端传 <c>false</c>）。</param>
        public SaveStorageCapabilities(bool supportsAtomicRename, bool supportsTrueAsyncIO, long maxRecommendedSize, bool volatileStorage, bool syncReadsAuthoritative)
        {
            SupportsAtomicRename = supportsAtomicRename;
            SupportsTrueAsyncIO = supportsTrueAsyncIO;
            MaxRecommendedSize = maxRecommendedSize;
            VolatileStorage = volatileStorage;
            SyncReadsAuthoritative = syncReadsAuthoritative;
        }
    }
}
