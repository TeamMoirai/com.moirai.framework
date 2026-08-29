using System;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池契约。
    /// <para>池由 <see cref="ObjectPoolService.GetOrCreatePool{T}"/> 创建；对象由外部构造并 <c>Register</c> 入池。</para>
    /// </summary>
    /// <typeparam name="T">池化对象类型。</typeparam>
    public interface IObjectPool<T> where T : ObjectBase
    {
        /// <summary>
        /// 获取池名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取池全名（类型名[.池名]）。
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// 获取对象类型。
        /// </summary>
        Type ObjectType { get; }

        /// <summary>
        /// 获取池内对象总数。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 获取是否允许同一对象被多次取用（引用计数模式）。
        /// </summary>
        bool AllowMultiSpawn { get; }

        /// <summary>
        /// 获取或设置超容自动释放间隔（秒）。
        /// </summary>
        float AutoReleaseInterval { get; set; }

        /// <summary>
        /// 获取或设置池容量（超出部分标记释放）。
        /// </summary>
        int Capacity { get; set; }

        /// <summary>
        /// 获取或设置空闲过期时间（秒）。
        /// </summary>
        float ExpireTime { get; set; }

        /// <summary>
        /// 获取或设置池优先级。
        /// </summary>
        int Priority { get; set; }

        /// <summary>
        /// 注册对象入池。
        /// </summary>
        /// <param name="obj">池化对象。</param>
        /// <param name="spawned">是否立即取用。</param>
        /// <returns>是否成功。</returns>
        bool Register(T obj, bool spawned);

        /// <summary>
        /// 取用一个对象（无名）。
        /// </summary>
        /// <returns>对象；无可复用对象返回 null。</returns>
        T Spawn();

        /// <summary>
        /// 按名取用一个对象。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <returns>对象；无可复用对象返回 null。</returns>
        T Spawn(string name);

        /// <summary>
        /// 归还对象。
        /// </summary>
        /// <param name="obj">池化对象。</param>
        void Despawn(T obj);

        /// <summary>
        /// 按引用目标归还对象。
        /// </summary>
        /// <param name="target">引用目标。</param>
        void DespawnTarget(object target);

        /// <summary>
        /// 释放全部可释放对象（含锁定外的全部空闲对象）。
        /// </summary>
        void Release();

        /// <summary>
        /// 释放指定数量的空闲对象。
        /// </summary>
        /// <param name="toReleaseCount">释放数量。</param>
        void Release(int toReleaseCount);

        /// <summary>
        /// 释放全部未使用且可释放的对象。
        /// </summary>
        void ReleaseAllUnused();
    }
}
