using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// YooAsset 后端的记录内核宿主面：三个原生句柄算子的后端实现；懒建、接线与转发全在 <see cref="ResourceRecordHostHandler"/>。
    /// </summary>
    partial class YooAssetHandler
    {
        #region 原生句柄算子 [NATIVE HANDLE OPERATORS]

        /// <inheritdoc />
        protected override bool IsHandleValid(object handle)
        {
            return handle is HandleBase { IsValid: true };
        }

        /// <inheritdoc />
        protected override void DisposeHandle(object handle)
        {
            if (handle is HandleBase { IsValid: true } valid)
            {
                valid.Dispose();
            }
        }

        /// <inheritdoc />
        protected override Sprite GetSubSprite(object handle, string spriteName)
        {
            return (handle as SubAssetsHandle)?.GetSubAssetObject<Sprite>(spriteName);
        }

        #endregion
    }
}
