using System.Collections.Generic;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    #region 远端资源服务 [RemoteService]

    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    internal class RemoteService : IRemoteService
    {
        private readonly string _defaultHostPrefix;
        private readonly string _fallbackHostPrefix;

        public RemoteService(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostPrefix = NormalizeHostPrefix(defaultHostServer);
            _fallbackHostPrefix = string.IsNullOrEmpty(fallbackHostServer)
                ? null
                : NormalizeHostPrefix(fallbackHostServer);
        }

        /// <remarks>
        /// 返回的列表会被下载操作跨帧持有并在重试时二次读取，因此每次调用必须给出独立数组：
        /// 并发下载下复用同一字段会让在途操作读到后一个文件的 URL。
        /// </remarks>
        IReadOnlyList<string> IRemoteService.GetRemoteUrls(string fileName)
        {
            string primaryUrl = StringUtility.Concat(_defaultHostPrefix, fileName);
            return _fallbackHostPrefix == null
                ? new[] { primaryUrl }
                : new[] { primaryUrl, StringUtility.Concat(_fallbackHostPrefix, fileName) };
        }

        private static string NormalizeHostPrefix(string hostServer)
        {
            if (string.IsNullOrEmpty(hostServer))
            {
                return string.Empty;
            }

            return hostServer[^1] == '/' ? hostServer : StringUtility.Concat(hostServer, "/");
        }
    }

    #endregion
}