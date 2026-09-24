using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核看向后端的唯一一面——三个原生句柄算子，加三个配置读数。
    /// <para>句柄在内核里以 <see cref="object"/> 存放（后端句柄都是引用类型，不装箱），本接口负责
    /// 校验、释放与按名取子精灵；配置三项刻意走属性活读而不是构造时传值，因为它们在运行期可写。</para>
    /// </summary>
    internal interface IResourceRecordHost
    {
        bool IsHandleValid(object handle);

        void DisposeHandle(object handle);

        Sprite GetSubSprite(object handle, string spriteName);

        int IdleAssetCapacity { get; }

        float IdleAssetExpireTime { get; }

        int AssetRecordCapacity { get; }
    }
}
