using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源系统运行模式（框架通用，与具体资源后端无关）。
    /// </summary>
    public enum EResourcePlayMode : byte
    {
        /// <summary>
        /// 编辑器模拟模式（仅编辑器内有效，运行时自动回退为 Offline）。
        /// </summary>
        EditorSimulate = 0,

        /// <summary>
        /// 单机离线模式。
        /// </summary>
        Offline = 1,

        /// <summary>
        /// 联机运行模式（远程资源服务器）。
        /// </summary>
        HostPlay = 2,

        /// <summary>
        /// WebGL 运行模式。
        /// </summary>
        WebPlay = 3,
    }

    /// <summary>
    /// 资源清理模式。
    /// </summary>
    public enum EResourceClearMode : byte
    {
        /// <summary>
        /// 清理所有缓存文件。
        /// </summary>
        ClearAllBundleFiles = 0,

        /// <summary>
        /// 清理未使用的缓存文件。
        /// </summary>
        ClearUnusedBundleFiles = 1,

        /// <summary>
        /// 清理过期的缓存文件。
        /// </summary>
        ClearWhenBundleFilesObsolete = 2,
    }

    /// <summary>
    /// WebGL 平台资源加载方式。
    /// </summary>
    public enum EResourceLoadWayWebGL : byte
    {
        /// <summary>
        /// 未定义。
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// 加载本地资源。
        /// </summary>
        Local = 1,

        /// <summary>
        /// 加载远程资源。
        /// </summary>
        /// <remarks>WebGL 平台支持本地资源判断。</remarks>
        Remote = 2,
    }

    /// <summary>
    /// 资源存在性检查结果。
    /// </summary>
    public enum EResourceHasAssetResult : byte
    {
        /// <summary>
        /// 资源不存在。
        /// </summary>
        AssetNotExist = 0,

        /// <summary>
        /// 资源存在且为原生资源。
        /// </summary>
        AssetExistRaw = 1,

        /// <summary>
        /// 资源存在。
        /// </summary>
        AssetExist = 2,

        /// <summary>
        /// 资源存在但为目录。
        /// </summary>
        AssetExistDirectory = 3,

        /// <summary>
        /// 定位地址无效。
        /// </summary>
        InvalidLocation = 4,
    }

    /// <summary>
    /// 资源信息（框架通用，描述单个资源条目）。
    /// </summary>
    public struct ResourceAssetInfoEntry
    {
        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string Location;

        /// <summary>
        /// 资源类型名称。
        /// </summary>
        public string TypeName;

        /// <summary>
        /// 资源标签集合。
        /// </summary>
        public string[] Tags;

        /// <summary>
        /// 资源大小（字节）。
        /// </summary>
        public long Size;

        /// <summary>
        /// 是否需要从远端下载。
        /// </summary>
        public bool NeedDownload;
    }

    /// <summary>
    /// 资源异步操作接口（框架通用，抽象各资源系统的异步操作）。
    /// </summary>
    public interface IResourceOperation
    {
        /// <summary>
        /// 是否完成。
        /// </summary>
        bool IsDone { get; }

        /// <summary>
        /// 进度（0-1）。
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// 是否成功。
        /// </summary>
        bool Succeed { get; }

        /// <summary>
        /// 错误信息（失败时非空）。
        /// </summary>
        string Error { get; }
    }

    /// <summary>
    /// 资源包初始化结果（框架通用）。
    /// </summary>
    public sealed class ResourcePackageInitResult
    {
        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 初始化操作。
        /// </summary>
        public IResourceOperation Operation;

        /// <summary>
        /// 操作完成后是否成功。
        /// </summary>
        public bool Succeed => Operation?.Succeed ?? false;
    }

    /// <summary>
    /// 资源下载器接口（框架通用，抽象各资源系统的下载器）。
    /// </summary>
    public interface IResourceDownloader
    {
        /// <summary>
        /// 是否完成。
        /// </summary>
        bool IsDone { get; }

        /// <summary>
        /// 是否成功。
        /// </summary>
        bool Succeed { get; }

        /// <summary>
        /// 错误信息（失败时非空）。
        /// </summary>
        string Error { get; }

        /// <summary>
        /// 总下载文件数。
        /// </summary>
        int TotalDownloadCount { get; }

        /// <summary>
        /// 下载失败列表。
        /// </summary>
        string[] FailedFiles { get; }

        /// <summary>
        /// 总下载大小（字节）。
        /// </summary>
        long TotalDownloadBytes { get; }

        /// <summary>
        /// 当前已完成的下载大小（字节）。
        /// </summary>
        long CurrentDownloadBytes { get; }

        /// <summary>
        /// 下载进度（0-1）。
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// 设置同时下载的最大数。
        /// </summary>
        int DownloadingMaxNumber { set; }

        /// <summary>
        /// 设置下载失败重试次数。
        /// </summary>
        int FailedTryAgain { set; }

        /// <summary>
        /// 开始下载。
        /// </summary>
        void BeginDownload();

        /// <summary>
        /// 暂停下载。
        /// </summary>
        void PauseDownload();

        /// <summary>
        /// 取消下载。
        /// </summary>
        void CancelDownload();
    }

    /// <summary>
    /// 包版本请求结果（框架通用）。
    /// </summary>
    public class ResourcePackageVersionResult
    {
        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string PackageName;

        private string _packageVersion;

        /// <summary>
        /// 包版本号。
        /// </summary>
        /// <remarks>
        /// 异步后端在操作完成前无法得知版本号——默认实现保存调用时的快照值；
        /// 后端应派生并覆写为实时透读底层操作（推荐），避免调用方在操作完成后仍取到创建期的过期空值。
        /// </remarks>
        public virtual string PackageVersion
        {
            get => _packageVersion;
            set => _packageVersion = value;
        }

        /// <summary>
        /// 请求操作。
        /// </summary>
        public IResourceOperation Operation;
    }

    /// <summary>
    /// 清理缓存结果（框架通用）。
    /// </summary>
    public sealed class ResourceClearCacheResult
    {
        /// <summary>
        /// 清理操作。
        /// </summary>
        public IResourceOperation Operation;

        /// <summary>
        /// 清理的文件数量。
        /// </summary>
        public int ClearedCount;
    }

    /// <summary>
    /// 资源加载回调集合（框架通用，替代各资源系统的回调结构）。
    /// </summary>
    public sealed class ResourceLoadCallbacks
    {
        /// <summary>
        /// 加载成功回调（location, asset, duration, userData）。
        /// </summary>
        public Action<string, UnityEngine.Object, float, object> LoadAssetSuccessCallback;

        /// <summary>
        /// 加载失败回调（location, errorMessage, userData）。
        /// </summary>
        public Action<string, string, object> LoadAssetFailureCallback;

        /// <summary>
        /// 加载进度回调（location, progress, userData）。
        /// </summary>
        public Action<string, float, object> LoadAssetUpdateCallback;

        /// <summary>
        /// 创建资源加载回调集合。
        /// </summary>
        /// <param name="loadAssetSuccessCallback">加载成功回调。</param>
        /// <param name="loadAssetFailureCallback">加载失败回调。</param>
        public ResourceLoadCallbacks(Action<string, UnityEngine.Object, float, object> loadAssetSuccessCallback, Action<string, string, object> loadAssetFailureCallback)
        {
            LoadAssetSuccessCallback = loadAssetSuccessCallback;
            LoadAssetFailureCallback = loadAssetFailureCallback;
        }
    }
}
