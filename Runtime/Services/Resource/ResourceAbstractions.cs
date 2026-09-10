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
        OfflinePlay = 1,

        /// <summary>
        /// 联机运行模式（远程资源服务器）。
        /// </summary>
        HostPlay = 2,

        /// <summary>
        /// WebGL 运行模式。
        /// </summary>
        WebGLPlay = 3,
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
        NotExist = 0,

        /// <summary>
        /// 资源存在但需要从远端更新下载。
        /// </summary>
        AssetOnline = 1,

        /// <summary>
        /// 资源存在且已存储在磁盘上。
        /// </summary>
        AssetOnDisk = 2,
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
    /// 资源系统场景句柄抽象（框架通用）——封装一次场景加载操作及其生命周期。
    /// <para>由具体资源后端（YooAsset、Addressable 等）适配实现，<see cref="ResourceServiceHandler.LoadSceneAsync"/> 创建，
    /// <see cref="ResourceService"/> 外观转发，供场景服务（SceneService）后端驱动主/子场景加载、激活、挂起恢复与卸载。</para>
    /// <para>句柄失效安全：释放（<see cref="Release"/>）或卸载完成后访问属性返回默认值，不抛出异常。</para>
    /// </summary>
    public abstract class ResourceSceneHandle
    {
        /// <summary>
        /// 场景加载是否完成。
        /// <para>挂起加载（suspendLoad）时加载进度停留于待激活状态，<see cref="IsDone"/> 保持 false，直至 <see cref="UnSuspend"/> 解除挂起。</para>
        /// </summary>
        public abstract bool IsDone { get; }

        /// <summary>
        /// 加载进度（0-1）。
        /// </summary>
        public abstract float Progress { get; }

        /// <summary>
        /// 错误信息（失败时非空）。
        /// </summary>
        public abstract string Error { get; }

        /// <summary>
        /// 已加载的场景对象（加载完成前为默认值）。
        /// </summary>
        public abstract UnityEngine.SceneManagement.Scene SceneObject { get; }

        /// <summary>
        /// 解除挂起，允许场景激活。
        /// </summary>
        /// <returns>是否解除成功。</returns>
        public abstract bool UnSuspend();

        /// <summary>
        /// 激活场景（设为当前活动场景）。
        /// </summary>
        /// <returns>是否激活成功。</returns>
        public abstract bool ActivateScene();

        /// <summary>
        /// 异步卸载场景（仅限 Additive 子场景）。卸载成功后句柄自动失效。
        /// </summary>
        /// <returns>卸载操作。</returns>
        public abstract IResourceOperation UnloadAsync();

        /// <summary>
        /// 释放场景句柄引用（不卸载场景）——用于 Single 主场景被替换后回收底层资源引用计数。
        /// </summary>
        public abstract void Release();
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

        /// <summary>
        /// 包版本号。
        /// </summary>
        /// <remarks>
        /// 异步后端在操作完成前无法得知版本号——默认实现保存调用时的快照值；
        /// 后端应派生并覆写为实时透读底层操作（推荐），避免调用方在操作完成后仍取到创建期的过期空值。
        /// </remarks>
        public virtual string PackageVersion { get; set; }

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
}
