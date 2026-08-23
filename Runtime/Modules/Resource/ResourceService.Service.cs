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
        private readonly string[] _urls;

        public RemoteService(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostPrefix = NormalizeHostPrefix(defaultHostServer);
            _fallbackHostPrefix = string.IsNullOrEmpty(fallbackHostServer)
                ? null
                : NormalizeHostPrefix(fallbackHostServer);
            _urls = _fallbackHostPrefix == null ? new string[1] : new string[2];
        }

        IReadOnlyList<string> IRemoteService.GetRemoteUrls(string fileName)
        {
            _urls[0] = StringUtility.Concat(_defaultHostPrefix, fileName);
            if (_fallbackHostPrefix != null)
            {
                _urls[1] = StringUtility.Concat(_fallbackHostPrefix, fileName);
            }

            return _urls;
        }

        private static string NormalizeHostPrefix(string hostServer)
        {
            if (string.IsNullOrEmpty(hostServer))
            {
                return string.Empty;
            }

            return hostServer[hostServer.Length - 1] == '/'
                ? hostServer
                : StringUtility.Concat(hostServer, "/");
        }
    }

    #endregion
}