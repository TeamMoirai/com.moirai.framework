namespace Moirai.Atropos
{
    /// <summary>
    /// 资源引用类型
    /// </summary>
    /// todo 所有 Asset Model 考虑是否要支持多种资源类型
    public enum AssetRefType
    {
        /// <summary>
        /// 直接引用
        /// </summary>
        /// <remarks>一般用于内置资源</remarks>
        Direct,
        /// <summary>
        /// 完整路径引用
        /// </summary>
        /// <remarks>一般用于YooAsset资源</remarks>
        FullPath,
        /// <summary>
        /// 名称引用
        /// </summary>
        /// <remarks>一般用于Addressable资源</remarks>
        Name
    }
}