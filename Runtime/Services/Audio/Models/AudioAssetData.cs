using System;

namespace Moirai.Atropos.Audio
{
    public class AudioAssetData : MemoryObject
    {
        /// <summary>
        /// 资源句柄（后端原生句柄的 object 包装）。
        /// </summary>
        public object AssetOperationHandle { private set; get; }

        /// <summary>
        /// 是否使用对象池。
        /// </summary>
        public bool InPool { private set; get; }

        /// <summary>
        /// 清理音频数据。
        /// </summary>
        public override void Clear()
        {
            AssetOperationHandle = null;
            InPool = false;
        }

        /// <summary>
        /// 生成音频数据。
        /// </summary>
        /// <param name="assetHandle">资源操作句柄（object 包装）。</param>
        /// <param name="inPool">是否使用对象池。</param>
        /// <returns>音频数据。</returns>
        internal static AudioAssetData Alloc(object assetHandle, bool inPool)
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
                ReleaseHandle(audioAssetData.AssetOperationHandle);
            }

            audioAssetData.InPool = false;
            audioAssetData.AssetOperationHandle = null;

            MemoryPool.Release(audioAssetData);
        }

        /// <summary>
        /// 释放句柄包装对象持有的租约/原生句柄。
        /// </summary>
        private static void ReleaseHandle(object handleObj)
        {
            if (handleObj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
