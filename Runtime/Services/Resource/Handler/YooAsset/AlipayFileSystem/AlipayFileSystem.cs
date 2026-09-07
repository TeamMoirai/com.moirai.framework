#if UNITY_WEBGL && UNITY_ALIMINIGAME
using YooAsset;

/// <summary>
/// 支付宝小游戏文件系统创建器。
/// </summary>
public static class AlipayFileSystemCreater
{
    /// <summary>
    /// 创建支付宝小游戏平台的文件系统参数。
    /// </summary>
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        return fileSystemParams;
    }
    /// <summary>
    /// 创建支付宝小游戏平台的文件系统参数，并指定 AssetBundle 解密器。
    /// </summary>
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService, IBundleDecryptor assetBundleDecryptor)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        fileSystemParams.AddParameter(EFileSystemParameter.AssetBundleDecryptor, assetBundleDecryptor);
        return fileSystemParams;
    }
    /// <summary>
    /// 创建支付宝小游戏平台的文件系统参数，并指定 AssetBundle 解密器和原生资源包解密器。
    /// </summary>
    public static FileSystemParameters CreateFileSystemParameters(IRemoteService remoteService, IBundleDecryptor assetBundleDecryptor, IBundleDecryptor rawBundleDecryptor)
    {
        var fileSystemParams = CreateBaseFileSystemParameters(remoteService);
        fileSystemParams.AddParameter(EFileSystemParameter.AssetBundleDecryptor, assetBundleDecryptor);
        fileSystemParams.AddParameter(EFileSystemParameter.RawBundleDecryptor, rawBundleDecryptor);
        return fileSystemParams;
    }

    private static FileSystemParameters CreateBaseFileSystemParameters(IRemoteService remoteService)
    {
        var fileSystemParams = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remoteService, true);
        fileSystemParams.AddParameter(EFileSystemParameter.WebPlatformStrategy, new AlipayPlatform());
        return fileSystemParams;
    }
}
#endif
