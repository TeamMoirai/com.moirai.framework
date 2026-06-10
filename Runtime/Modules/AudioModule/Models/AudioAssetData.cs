using YooAsset;

namespace Moirai.Atropos.Audio
{
    public class AudioAssetData : MemoryObject
    {
        /// <summary>
        /// 资源句柄。
        /// </summary>
        public AssetHandle AssetOperationHandle { private set; get; }

        /// <summary>
        /// 是否使用对象池。
        /// </summary>
        public bool InPool { private set; get; }

        public override void InitFromPool() { }

        /// <summary>
        /// 回收到对象池。
        /// </summary>
        public override void RecycleToPool()
        {
            if (!InPool)
            {
                AssetOperationHandle.Dispose();
            }

            InPool = false;
            AssetOperationHandle = null;
        }

        /// <summary>
        /// 生成音频数据。
        /// </summary>
        /// <param name="assetHandle">资源操作句柄。</param>
        /// <param name="inPool">是否使用对象池。</param>
        /// <returns>音频数据。</returns>
        internal static AudioAssetData Alloc(AssetHandle assetHandle, bool inPool)
        {
            AudioAssetData ret = MemoryPool.Acquire<AudioAssetData>();
            ret.AssetOperationHandle = assetHandle;
            ret.InPool = inPool;
            ret.InitFromPool();
            return ret;
        }

        /// <summary>
        /// 回收音频数据。
        /// </summary>
        /// <param name="audioAssetData"></param>
        internal static void DeAlloc(AudioAssetData audioAssetData)
        {
            if (audioAssetData != null)
            {
                MemoryPool.Release(audioAssetData);
                audioAssetData.RecycleToPool();
            }
        }
    }
}