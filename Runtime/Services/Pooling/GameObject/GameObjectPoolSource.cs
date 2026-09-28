using System;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池化来源键：资源地址或外部 Prefab 引用。
    /// <para>同一套 Spawn/Despawn/Warmup/Flush API 适配两种来源；string / GameObject 可隐式转换。</para>
    /// <para><see cref="Group"/> 仅在 Prefab 源首次建池时生效；Location 源的分组以 PoolConfig 规则为准。</para>
    /// <para><c>default</c> 为无效源（IsValid=false），外观入口统一 fail-safe 返回空。</para>
    /// </summary>
    public readonly struct GameObjectPoolSource : IEquatable<GameObjectPoolSource>
    {
        #region 字段 [FIELDS]

        private readonly string _location;
        private readonly GameObject _prefab;
        private readonly string _group;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 是否为外部 Prefab 源。
        /// </summary>
        public bool IsPrefab => _prefab != null;

        /// <summary>
        /// 是否为有效来源（Prefab 引用非空，或 Location 非空）。
        /// </summary>
        public bool IsValid => _prefab != null || !string.IsNullOrEmpty(_location);

        /// <summary>
        /// 资源地址（Prefab 源时为 null）。
        /// </summary>
        public string Location => _location;

        /// <summary>
        /// 外部 Prefab 引用（Location 源时为 null）。
        /// </summary>
        public GameObject Prefab => _prefab;

        /// <summary>
        /// 分组提示。仅 Prefab 源建池时生效。
        /// </summary>
        public string Group => _group;

        #endregion

        #region 构造 [CONSTRUCTOR]

        private GameObjectPoolSource(string location, GameObject prefab, string group)
        {
            _location = location;
            _prefab = prefab;
            _group = group;
        }

        #endregion

        #region 工厂 [FACTORY]

        /// <summary>
        /// 由资源地址创建来源。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>池化来源。</returns>
        public static GameObjectPoolSource FromLocation(string location) =>
            new GameObjectPoolSource(location, null, null);

        /// <summary>
        /// 由外部 Prefab 引用创建来源。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        /// <param name="group">建池分组（仅首次建池生效）。</param>
        /// <returns>池化来源。</returns>
        public static GameObjectPoolSource FromPrefab(GameObject prefab, string group = null) =>
            new GameObjectPoolSource(null, prefab, group);

        #endregion

        #region 隐式转换 [IMPLICIT CONVERSION]

        /// <summary>
        /// 资源地址隐式转换。
        /// </summary>
        /// <param name="location">资源地址。</param>
        public static implicit operator GameObjectPoolSource(string location) => FromLocation(location);

        /// <summary>
        /// 外部 Prefab 隐式转换。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        public static implicit operator GameObjectPoolSource(GameObject prefab) => FromPrefab(prefab);

        #endregion

        #region 相等性 [EQUALITY]

        /// <summary>
        /// 与另一来源比较。Prefab 用引用身份（避免 Unity 假空污染哈希）；Location 用 Ordinal。
        /// </summary>
        /// <param name="other">另一来源。</param>
        /// <returns>是否相等。</returns>
        public bool Equals(GameObjectPoolSource other)
        {
            if (!ReferenceEquals(_prefab, other._prefab))
            {
                return false;
            }

            return string.Equals(_location, other._location, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is GameObjectPoolSource other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // 与 Equals 同用引用身份，避免 Unity 假空把 hash 污染为 0。
            int hash = ReferenceEquals(_prefab, null) ? 0 : _prefab.GetInstanceID();
            return string.IsNullOrEmpty(_location) ? hash : (hash * 397) ^ _location.GetHashCode();
        }

        /// <summary>
        /// 相等运算符。
        /// </summary>
        /// <param name="left">左值。</param>
        /// <param name="right">右值。</param>
        /// <returns>是否相等。</returns>
        public static bool operator ==(GameObjectPoolSource left, GameObjectPoolSource right) => left.Equals(right);

        /// <summary>
        /// 不等运算符。
        /// </summary>
        /// <param name="left">左值。</param>
        /// <param name="right">右值。</param>
        /// <returns>是否不等。</returns>
        public static bool operator !=(GameObjectPoolSource left, GameObjectPoolSource right) => !left.Equals(right);

        #endregion
    }
}
