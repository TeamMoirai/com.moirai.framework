using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// REST 通用云端 KV 存储（HttpClient 纯 .NET 传输，可在任意线程调用；不触达 Unity 主线程 API）。
    /// <para>框架定义的极简 REST 契约，服务端按契约实现即可接入（路径段逐段 URL 转义，<c>/</c> 为路径分隔符）：</para>
    /// <para>　读：<c>GET {baseUrl}/{keyPrefix}{key}</c> → 200 载荷字节，响应头 <c>X-Save-Revision</c>（long，修订号）/
    /// <c>ETag</c>（回退通道，引号包裹的 long）与 <c>Last-Modified</c>（远端权威时间戳）；404 = 缺档返回 <c>null</c>。</para>
    /// <para>　写：<c>PUT {baseUrl}/{keyPrefix}{key}</c>（application/octet-stream 原始字节）→ 2xx，响应头同上返回新修订号（无则 0 = 无版本通道）。</para>
    /// <para>　删：<c>DELETE {baseUrl}/{keyPrefix}{key}</c> → 2xx/404 均视为成功（幂等）。</para>
    /// <para>　存在：<c>HEAD {baseUrl}/{keyPrefix}{key}</c> → 200/404。</para>
    /// <para>　枚举：<c>GET {baseUrl}?prefix={keyPrefix}{prefix}</c>（服务端前缀过滤下推）→ 200 JSON 数组
    /// <c>[{"key":"完整键（含 keyPrefix）","size":123,"modified":"ISO8601","revision":42}]</c>；modified/revision 可缺省（0/未知）。
    /// 返回键须携带 keyPrefix（本端剥离后交还调用方），前缀外键被防御性跳过。</para>
    /// <para>超时/非约定状态码/网络失败一律抛异常（远端失败语义——<see cref="CloudSaveStorageBackend"/> 归一为离线降级）；
    /// 用户取消经 <paramref name="cancellationToken"/> 传播 <see cref="OperationCanceledException"/>（不视为远端失败）。</para>
    /// <para>平台限制：HttpClient 依赖 raw socket——WebGL 不可用（WebGL 项目请使用 UGS 后端或 UnityWebRequest 自定义实现）。</para>
    /// </summary>
    [Serializable]
    public class RestCloudSaveKvStore : CloudSaveKvStore
    {
        #region 配置 [CONFIGURATION]

        /// <summary>服务端点根地址（如 <c>https://save.example.com/v1/kv</c>；末尾斜杠自动剥离）。</summary>
        [Tooltip("服务端点根地址（如 https://save.example.com/v1/kv）。契约见类文档。")]
        [SerializeField] private string m_BaseUrl = "";

        /// <summary>云端键前缀（多租户/多游戏共端点时的命名空间，如 <c>tenant1/</c>；可空）。</summary>
        [Tooltip("云端键前缀（多租户命名空间，如 tenant1/；可空）。枚举返回键须含此前缀（本端剥离）。")]
        [SerializeField] private string m_KeyPrefix = "";

        /// <summary>认证头名称（如 <c>Authorization</c>/<c>X-Api-Key</c>；空 = 不携带认证头）。</summary>
        [Tooltip("认证头名称（如 Authorization / X-Api-Key；空 = 不携带认证头）。")]
        [SerializeField] private string m_AuthHeaderName = "";

        /// <summary>认证头静态值（<see cref="AuthTokenProvider"/> 注入的动态令牌优先）。</summary>
        [Tooltip("认证头静态值。安全提示：静态密钥随设置资产入库——生产环境建议经代码注入 AuthTokenProvider 提供动态令牌。")]
        [SerializeField] private string m_AuthHeaderValue = "";

        /// <summary>单次远端请求超时（秒；超时抛 <see cref="TimeoutException"/>——远端失败语义）。</summary>
        [Tooltip("单次远端请求超时（秒）。")]
        [SerializeField, Min(1)] private int m_TimeoutSeconds = 15;

        /// <summary>端点根地址注入点（测试/代码装配用；Inspector 配置走序列化字段）。</summary>
        internal string BaseUrl { set => m_BaseUrl = value; }

        /// <summary>云端键前缀注入点（测试/代码装配用）。</summary>
        internal string KeyPrefix { set => m_KeyPrefix = value; }

        /// <summary>认证头名称注入点（测试/代码装配用）。</summary>
        internal string AuthHeaderName { set => m_AuthHeaderName = value; }

        /// <summary>认证头静态值注入点（测试/代码装配用）。</summary>
        internal string AuthHeaderValue { set => m_AuthHeaderValue = value; }

        /// <summary>请求超时注入点（测试/代码装配用）。</summary>
        internal int TimeoutSeconds { set => m_TimeoutSeconds = value; }

        #endregion

        #region 运行时状态 [RUNTIME STATE]

        /// <summary>修订号响应头名（首选版本通道；缺失回退 ETag）。</summary>
        private const string REVISION_HEADER_NAME = "X-Save-Revision";

        /// <summary>动态认证令牌提供方（代码注入——如登录后 JWT；优先于静态值，不入库不入序列化）。</summary>
        [NonSerialized] internal Func<string> AuthTokenProvider;

        /// <summary>测试用消息处理器工厂（非空时每实例自建 HttpClient 走假处理器，不触真实网络；生产为 <c>null</c> 共享静态实例）。</summary>
        internal static Func<HttpMessageHandler> s_MessageHandlerFactoryForTests;

        /// <summary>每实例懒建客户端（仅测试工厂路径使用）。</summary>
        [NonSerialized] private HttpClient _client;

        /// <summary>生产共享客户端（进程生命周期——避免逐实例 socket 耗尽）。</summary>
        private static HttpClient s_SharedClient;

        /// <summary>共享客户端懒建锁。</summary>
        private static readonly object s_ClientLock = new object();

        #endregion

        #region 远端操作 [REMOTE OPERATIONS]

        /// <summary>
        /// 读取远端条目（GET；404 返回 <c>null</c>；超时/远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>条目（含远端时间戳与修订号）；缺档为 <c>null</c>。</returns>
        public override async UniTask<CloudKvEntry?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendCoreAsync(HttpMethod.Get, BuildItemUrl(key), null, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                EnsureSuccess(response, "Read", key);
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                return new CloudKvEntry(bytes, ResolveRemoteTimestamp(response), ResolveRemoteRevision(response));
            }
        }

        /// <summary>
        /// 远端是否存在目标键（HEAD；远端失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在返回 <c>true</c>。</returns>
        public override async UniTask<bool> ExistsAsync(string key, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendCoreAsync(HttpMethod.Head, BuildItemUrl(key), null, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                EnsureSuccess(response, "Exists", key);
                return true;
            }
        }

        /// <summary>
        /// 写入远端条目（PUT 整值替换；远端失败抛异常；修订号取响应头 <c>X-Save-Revision</c>/<c>ETag</c>，无则 0）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="bytes">载荷字节。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>远端分配的单调修订号（<c>0</c> = 服务端未提供，同步裁决回退时间戳比较）。</returns>
        public override async UniTask<long> WriteAsync(string key, byte[] bytes, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendCoreAsync(HttpMethod.Put, BuildItemUrl(key), bytes, cancellationToken))
            {
                EnsureSuccess(response, "Write", key);
                return ResolveRemoteRevision(response);
            }
        }

        /// <summary>
        /// 删除远端条目（DELETE；2xx/404 均成功——幂等；其余失败抛异常）。
        /// </summary>
        /// <param name="key">云端键。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>删除完成的异步任务。</returns>
        public override async UniTask DeleteAsync(string key, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await SendCoreAsync(HttpMethod.Delete, BuildItemUrl(key), null, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }

                EnsureSuccess(response, "Delete", key);
            }
        }

        /// <summary>
        /// 枚举远端条目（GET 空前缀列表；远端失败抛异常）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>条目元信息数组。</returns>
        public override UniTask<CloudKvEntryInfo[]> EnumerateAsync(CancellationToken cancellationToken)
        {
            return EnumerateAsync(string.Empty, cancellationToken);
        }

        /// <summary>
        /// 枚举远端指定前缀下的条目（服务端前缀过滤下推 <c>?prefix=</c>；本端二次校验剥离 <see cref="m_KeyPrefix"/> 后交还）。
        /// </summary>
        /// <param name="prefix">键前缀（空 = 全部）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>前缀命中的条目元信息数组。</returns>
        public override async UniTask<CloudKvEntryInfo[]> EnumerateAsync(string prefix, CancellationToken cancellationToken)
        {
            string serverPrefix = StringUtility.Concat(Normalize(m_KeyPrefix), prefix ?? string.Empty);
            string url = StringUtility.Concat(Normalize(m_BaseUrl).TrimEnd('/'), "?prefix=", Uri.EscapeDataString(serverPrefix));
            using (HttpResponseMessage response = await SendCoreAsync(HttpMethod.Get, url, null, cancellationToken))
            {
                EnsureSuccess(response, "Enumerate", prefix);
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseListEnvelope(json, prefix ?? string.Empty);
            }
        }

        #endregion

        #region 传输与解析 [TRANSPORT / PARSING]

        /// <summary>
        /// 发送一次远端请求（认证头注入 + 超时控制；超时转 <see cref="TimeoutException"/>，用户取消透传）。
        /// </summary>
        /// <param name="method">HTTP 方法。</param>
        /// <param name="url">完整请求地址。</param>
        /// <param name="body">请求体（<c>null</c> = 无）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>响应消息（调用方负责释放与状态码判定）。</returns>
        private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, byte[] body, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(m_BaseUrl))
            {
                throw new InvalidOperationException("[RestCloudSaveKvStore] BaseUrl is not configured.");
            }

            using (var request = new HttpRequestMessage(method, url))
            {
                string authValue = ResolveAuthValue();
                if (!string.IsNullOrEmpty(m_AuthHeaderName) && authValue != null)
                {
                    // 认证头值来自项目注入——跳过严格校验以兼容非标准令牌格式
                    request.Headers.TryAddWithoutValidation(m_AuthHeaderName, authValue);
                }

                if (body != null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                }

                using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutSource.CancelAfter(TimeSpan.FromSeconds(m_TimeoutSeconds));
                    try
                    {
                        return await ResolveClient().SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(StringUtility.Format("[RestCloudSaveKvStore] Request timed out after {0}s, url: {1}.", m_TimeoutSeconds, url));
                    }
                }
            }
        }

        /// <summary>
        /// 解析认证头值（动态令牌提供方优先，静态配置兜底；均无则 <c>null</c> 不携带）。
        /// </summary>
        private string ResolveAuthValue()
        {
            string provided = AuthTokenProvider?.Invoke();
            if (!string.IsNullOrEmpty(provided))
            {
                return provided;
            }

            return string.IsNullOrEmpty(m_AuthHeaderValue) ? null : m_AuthHeaderValue;
        }

        /// <summary>
        /// 解析可用 HttpClient（测试工厂优先——每实例独立；生产共享静态实例）。
        /// </summary>
        private HttpClient ResolveClient()
        {
            Func<HttpMessageHandler> factory = s_MessageHandlerFactoryForTests;
            if (factory != null)
            {
                return _client ??= new HttpClient(factory(), disposeHandler: true);
            }

            if (s_SharedClient == null)
            {
                lock (s_ClientLock)
                {
                    s_SharedClient ??= new HttpClient();
                }
            }

            return s_SharedClient;
        }

        /// <summary>
        /// 构造条目请求地址（<c>{base}/{prefix}{key}</c>，路径段逐段 URL 转义，<c>/</c> 保留为分隔符）。
        /// </summary>
        /// <param name="key">云端键。</param>
        private string BuildItemUrl(string key)
        {
            string baseUrl = Normalize(m_BaseUrl);
            string keyPrefix = Normalize(m_KeyPrefix);
            StringHandler.IStringBuilder builder = StringUtility.CreateStringBuilder(baseUrl.Length + keyPrefix.Length + key.Length + 8);
            builder.Append(baseUrl.TrimEnd('/'));
            builder.Append('/');
            AppendEscapedPath(builder, keyPrefix);
            AppendEscapedPath(builder, key);
            return builder.ToStringAndDispose();
        }

        /// <summary>
        /// 逐段转义追加路径（空段跳过——前缀可空；键内的 <c>/</c> 保留为路径分隔符）。
        /// </summary>
        private static void AppendEscapedPath(StringHandler.IStringBuilder builder, string path)
        {
            int segmentStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i != path.Length && path[i] != '/')
                {
                    continue;
                }

                if (i > segmentStart)
                {
                    builder.Append(Uri.EscapeDataString(path.Substring(segmentStart, i - segmentStart)));
                }

                if (i < path.Length)
                {
                    builder.Append('/');
                }

                segmentStart = i + 1;
            }
        }

        /// <summary>
        /// 解析远端修订号（<c>X-Save-Revision</c> 首选，<c>ETag</c> 回退；均无/非正整数 = 0 无版本通道）。
        /// </summary>
        private static long ResolveRemoteRevision(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues(REVISION_HEADER_NAME, out IEnumerable<string> values))
            {
                foreach (string value in values)
                {
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long revision) && revision > 0L)
                    {
                        return revision;
                    }
                }
            }

            EntityTagHeaderValue etag = response.Headers.ETag;
            if (etag != null && etag.Tag != null)
            {
                string tag = etag.Tag.Trim('"');
                if (long.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out long etagRevision) && etagRevision > 0L)
                {
                    return etagRevision;
                }
            }

            return 0L;
        }

        /// <summary>
        /// 解析远端时间戳（<c>Last-Modified</c> 首选，响应 <c>Date</c> 回退；均无 = 接收时刻——远端权威时钟语义下的最保守选择）。
        /// </summary>
        private static DateTime ResolveRemoteTimestamp(HttpResponseMessage response)
        {
            DateTimeOffset? timestamp = response.Content?.Headers?.LastModified ?? response.Headers.Date;
            return timestamp.HasValue ? timestamp.Value.UtcDateTime : DateTime.UtcNow;
        }

        /// <summary>
        /// 解析枚举 JSON 信封（剥离 <see cref="m_KeyPrefix"/>；前缀外键防御性跳过；modified 缺失/解析失败 = <see cref="DateTime.MinValue"/> 未知时间）。
        /// </summary>
        /// <param name="json">响应 JSON。</param>
        /// <param name="requestedPrefix">本次请求的键前缀（剥离 store 前缀后的校验基准）。</param>
        private CloudKvEntryInfo[] ParseListEnvelope(string json, string requestedPrefix)
        {
            string keyPrefix = Normalize(m_KeyPrefix);
            List<ListEntryDto> items = DefaultJson.FromJson<List<ListEntryDto>>(json);
            var results = new List<CloudKvEntryInfo>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ListEntryDto item = items[i];
                if (item?.key == null || !item.key.StartsWith(keyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string key = item.key.Substring(keyPrefix.Length);
                if (!key.StartsWith(requestedPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                DateTime modifiedUtc = DateTime.MinValue;
                if (!string.IsNullOrEmpty(item.modified)
                    && DateTimeOffset.TryParse(item.modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                {
                    modifiedUtc = parsed.UtcDateTime;
                }

                results.Add(new CloudKvEntryInfo(key, item.size, modifiedUtc, item.revision));
            }

            return results.ToArray();
        }

        /// <summary>
        /// 非成功状态码归一为远端失败异常（<see cref="CloudSaveStorageBackend"/> 据此离线降级）。
        /// </summary>
        private static void EnsureSuccess(HttpResponseMessage response, string operation, string key)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new System.IO.IOException(StringUtility.Format("[RestCloudSaveKvStore] {0} failed, key: {1}, status: {2}.", operation, key, (int)response.StatusCode));
            }
        }

        /// <summary>
        /// 序列化字段 null 防御（反序列化缺字段时为 <c>null</c>）。
        /// </summary>
        private static string Normalize(string value)
        {
            return value ?? string.Empty;
        }

        /// <summary>
        /// 枚举信封 DTO（字段名即 wire 键名；modified 为 ISO8601 字符串——解析失败容错由调用方承担）。
        /// </summary>
        [Serializable]
        private sealed class ListEntryDto
        {
            public string key;
            public long size;
            public string modified;
            public long revision;
        }

        #endregion
    }
}
