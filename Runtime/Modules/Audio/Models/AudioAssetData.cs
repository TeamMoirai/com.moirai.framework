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

        /// <summary>
        /// 清理音频数据。
        /// </summary>
        public override void Clear()
        {
            AssetOperationHandle = default;
            InPool = false;
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
            return ret;
        }

        /// <summary>
        /// 回收音频数据。
        /// </summary>
        /// <param name="audioAssetData"></param>
        internal static void Dealloc(AudioAssetData audioAssetData)
        {
            if (audioAssetData == null) return;

            if (!audioAssetData.InPool)
            {
                audioAssetData.AssetOperationHandle.Dispose();
            }

            audioAssetData.InPool = false;
            audioAssetData.AssetOperationHandle = null;

            MemoryPool.Release(audioAssetData);
        }
    }
}
