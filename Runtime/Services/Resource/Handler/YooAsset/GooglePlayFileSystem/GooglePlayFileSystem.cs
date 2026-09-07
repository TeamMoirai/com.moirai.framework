#if UNITY_ANDROID && GOOGLE_PLAY
using YooAsset;

/// <summary>
/// Google Play 文件系统创建器。
/// </summary>
public static class GooglePlayFileSystemCreater
{
    /// <summary>
    /// 创建 Google Play 文件系统参数，资源包通过 Play Asset Delivery 加载。
    /// </summary>
    public static FileSystemParameters CreateFileSystemParameters(string packageRoot)
    {
        string fileSystemClass = $"{nameof(GooglePlayFileSystem)},YooAsset.MiniGame";
        var fileSystemParams = new FileSystemParameters(fileSystemClass, packageRoot);
        return fileSystemParams;
    }
}

/// <summary>
/// Google Play Asset Delivery 文件系统。
/// 通过 PlayAssetDelivery 加载资源包，而非本地文件 I/O。
/// 参见：https://developer.android.com/guide/playcore/asset-delivery
/// </summary>
internal class GooglePlayFileSystem : BuiltinFileSystem, IFileSystem
{
    /// <summary>
    /// 重写资源包加载逻辑，改用 Play Asset Delivery。
    /// 重新实现 <see cref="IFileSystem.LoadPackageBundleAsync"/> 接口方法，
    /// 使接口分发命中本方法而非基类实现。
    /// </summary>
    public new FSLoadPackageBundleOperation LoadPackageBundleAsync(FSLoadPackageBundleOptions options)
    {
        var operation = new GPFSLoadPackageBundleOperation(this, options);
        return operation;
    }
}
#endif
