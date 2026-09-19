using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// REST 云端 KV 存储测试（内存假处理器经 <c>s_MessageHandlerFactoryForTests</c> 注入——不触真实网络）：
    /// 读写往返（修订号/时间戳）、存在探测、幂等删除、前缀枚举下推与键剥离、认证头（静态值/动态提供方优先）、
    /// 远端失败归一（500/超时）、ETag 修订号回退通道。
    /// <para>执行模型统一为 [UnityTest] + WaitForTask 协程（见 SaveEntityIncrementalTests 头注——禁止 async Task+AsTask）。</para>
    /// </summary>
    public class RestCloudSaveKvStoreTests
    {
        /// <summary>测试端点根地址（假处理器拦截——不解析不连接）。</summary>
        private const string FAKE_BASE_URL = "http://fake.local/saves/v1";

        /// <summary>测试端点在假处理器内的路径前缀。</summary>
        private const string FAKE_BASE_PATH = "/saves/v1";

        /// <summary>测试认证头名。</summary>
        private const string AUTH_HEADER = "X-Test-Auth";

        /// <summary>测试租户前缀。</summary>
        private const string TENANT_PREFIX = "tenant1/";

        private FakeRestMessageHandler _handler;
        private RestCloudSaveKvStore _store;

        [SetUp]
        public void SetUp()
        {
            _handler = new FakeRestMessageHandler();
            RestCloudSaveKvStore.s_MessageHandlerFactoryForTests = () => _handler;
            _store = new RestCloudSaveKvStore
            {
                BaseUrl = FAKE_BASE_URL,
                KeyPrefix = TENANT_PREFIX,
                AuthHeaderName = AUTH_HEADER,
                AuthHeaderValue = "static-token",
                TimeoutSeconds = 5,
            };
        }

        [TearDown]
        public void TearDown()
        {
            RestCloudSaveKvStore.s_MessageHandlerFactoryForTests = null;
            _handler = null;
            _store = null;
        }

        /// <summary>
        /// 内存 REST 假处理器（HttpMessageHandler 直通）：KV 字典 + 单调修订号 + 时钟偏移；故障/延迟/ETag 通道注入。
        /// </summary>
        private sealed class FakeRestMessageHandler : HttpMessageHandler
        {
            /// <summary>条目（载荷 + 修订号 + 远端权威时间戳）。</summary>
            public sealed class Entry
            {
                public byte[] Bytes;
                public long Revision;
                public DateTime ModifiedUtc;
            }

            /// <summary>远端仓库（键含租户前缀——模拟服务端真实键空间）。</summary>
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

            /// <summary>单调修订号发生器。</summary>
            public long NextRevision;

            /// <summary>远端时钟偏移（模拟云端权威时钟与客户端偏差）。</summary>
            public TimeSpan ClockOffset;

            /// <summary>全局故障注入（&gt;0 时全部请求返回该状态码）。</summary>
            public int InjectedStatusCode;

            /// <summary>延迟注入（客户端超时判定用——不可取消，客户端先行超时）。</summary>
            public TimeSpan InjectedDelay;

            /// <summary>修订号经 ETag 响应头回传（回退通道测试）。</summary>
            public bool RevisionViaETag;

            /// <summary>最近一次请求携带的认证头值（null = 未携带）。</summary>
            public string LastAuthValue;

            /// <summary>最近一次列表请求的服务端前缀（下推断言用）。</summary>
            public string LastListQueryPrefix;

            /// <summary>最近一次条目请求路径（租户前缀/转义断言用）。</summary>
            public string LastItemPath;

            /// <summary>列表信封 DTO（wire 键名与框架契约一致）。</summary>
            [Serializable]
            public sealed class ListDto
            {
                public string key;
                public long size;
                public string modified;
                public long revision;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (InjectedDelay > TimeSpan.Zero)
                {
                    // 协作取消——与真实 HttpClientHandler 行为一致（取消即中止 socket 抛 TaskCanceledException；
                    // HttpClient 不独立强制超时，令牌须由 handler 响应）
                    await Task.Delay(InjectedDelay, cancellationToken).ConfigureAwait(false);
                }

                LastAuthValue = request.Headers.TryGetValues(AUTH_HEADER, out IEnumerable<string> authValues)
                    ? string.Join(";", authValues)
                    : null;

                if (InjectedStatusCode > 0)
                {
                    return new HttpResponseMessage((HttpStatusCode)InjectedStatusCode);
                }

                string path = request.RequestUri.AbsolutePath;
                string query = request.RequestUri.Query;

                if (request.Method == HttpMethod.Get && !string.IsNullOrEmpty(query))
                {
                    return HandleList(query);
                }

                string key = path.StartsWith(FAKE_BASE_PATH + "/", StringComparison.Ordinal)
                    ? Uri.UnescapeDataString(path.Substring(FAKE_BASE_PATH.Length + 1))
                    : path;
                LastItemPath = key;

                if (request.Method == HttpMethod.Get)
                {
                    if (!Entries.TryGetValue(key, out Entry entry))
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }

                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(entry.Bytes) };
                    StampRevision(response, entry.Revision);
                    response.Content.Headers.LastModified = new DateTimeOffset(entry.ModifiedUtc, TimeSpan.Zero);
                    return response;
                }

                if (request.Method == HttpMethod.Head)
                {
                    return new HttpResponseMessage(Entries.ContainsKey(key) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
                }

                if (request.Method == HttpMethod.Put)
                {
                    byte[] bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    long revision = ++NextRevision;
                    Entries[key] = new Entry { Bytes = bytes, Revision = revision, ModifiedUtc = DateTime.UtcNow + ClockOffset };
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    StampRevision(response, revision);
                    return response;
                }

                if (request.Method == HttpMethod.Delete)
                {
                    Entries.Remove(key);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            private HttpResponseMessage HandleList(string query)
            {
                string prefix = ExtractQueryPrefix(query);
                LastListQueryPrefix = prefix;
                var dtos = new List<ListDto>();
                foreach (KeyValuePair<string, Entry> pair in Entries)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        dtos.Add(new ListDto
                        {
                            key = pair.Key,
                            size = pair.Value.Bytes.LongLength,
                            modified = pair.Value.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture),
                            revision = pair.Value.Revision,
                        });
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DefaultJson.ToJson(dtos)) };
            }

            private void StampRevision(HttpResponseMessage response, long revision)
            {
                if (RevisionViaETag)
                {
                    response.Headers.ETag = new EntityTagHeaderValue("\"" + revision.ToString(CultureInfo.InvariantCulture) + "\"");
                }
                else
                {
                    response.Headers.TryAddWithoutValidation("X-Save-Revision", revision.ToString(CultureInfo.InvariantCulture));
                }
            }

            private static string ExtractQueryPrefix(string query)
            {
                string trimmed = query.TrimStart('?');
                string[] parts = trimmed.Split('&');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("prefix=", StringComparison.Ordinal))
                    {
                        return Uri.UnescapeDataString(parts[i].Substring("prefix=".Length));
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// 主线程等待异步任务完成（逐帧 yield 保持编辑器泵——模式契约见 SaveEntityIncrementalTests）。
        /// </summary>
        private static IEnumerator WaitForTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            yield return null;

            if (task.IsFaulted && task.Exception != null)
            {
                throw task.Exception.InnerException ?? task.Exception;
            }
        }

        [UnityTest]
        public IEnumerator Read_MissingKey_ReturnsNull()
        {
            Task<CloudKvEntry?> task = _store.ReadAsync("Save/missing.sav", default).AsTask();
            yield return WaitForTask(task);

            Assert.IsFalse(task.Result.HasValue, "缺档应返回 null（404 非错误）");
            StringAssert.StartsWith(TENANT_PREFIX + "Save/", _handler.LastItemPath, "条目请求键须携带租户前缀");
        }

        [UnityTest]
        public IEnumerator WriteThenRead_RoundTripsBytesRevisionAndTimestamp()
        {
            _handler.ClockOffset = TimeSpan.FromMinutes(7);
            byte[] payload = { 1, 2, 3, 240, 15 };

            Task<long> writeTask = _store.WriteAsync("Save/slot1.sav", payload, default).AsTask();
            yield return WaitForTask(writeTask);
            Assert.AreEqual(1L, writeTask.Result, "首次写入应得修订号 1");

            DateTime remoteTime = _handler.Entries[TENANT_PREFIX + "Save/slot1.sav"].ModifiedUtc;

            Task<CloudKvEntry?> readTask = _store.ReadAsync("Save/slot1.sav", default).AsTask();
            yield return WaitForTask(readTask);

            Assert.IsTrue(readTask.Result.HasValue, "已写键应可读");
            CollectionAssert.AreEqual(payload, readTask.Result.Value.Bytes, "载荷字节应往返一致");
            Assert.AreEqual(1L, readTask.Result.Value.Version, "读回条目应携带远端修订号");
            Assert.AreEqual(remoteTime, readTask.Result.Value.LastWriteTimeUtc, "读回时间戳应为远端权威时钟（含偏移）");

            Task<long> rewriteTask = _store.WriteAsync("Save/slot1.sav", payload, default).AsTask();
            yield return WaitForTask(rewriteTask);
            Assert.AreEqual(2L, rewriteTask.Result, "重写应递增修订号");
        }

        [UnityTest]
        public IEnumerator Exists_MissThenHit()
        {
            Task<bool> missTask = _store.ExistsAsync("Save/none.sav", default).AsTask();
            yield return WaitForTask(missTask);
            Assert.IsFalse(missTask.Result, "缺档应 false");

            Task<long> writeTask = _store.WriteAsync("Save/none.sav", new byte[] { 9 }, default).AsTask();
            yield return WaitForTask(writeTask);

            Task<bool> hitTask = _store.ExistsAsync("Save/none.sav", default).AsTask();
            yield return WaitForTask(hitTask);
            Assert.IsTrue(hitTask.Result, "已写键应 true");
        }

        [UnityTest]
        public IEnumerator Delete_ExistingAndMissing_Idempotent()
        {
            Task<long> writeTask = _store.WriteAsync("Save/del.sav", new byte[] { 1 }, default).AsTask();
            yield return WaitForTask(writeTask);

            Task deleteHit = _store.DeleteAsync("Save/del.sav", default).AsTask();
            yield return WaitForTask(deleteHit);
            Assert.IsFalse(_handler.Entries.ContainsKey(TENANT_PREFIX + "Save/del.sav"), "已删键应移除");

            Task deleteMiss = _store.DeleteAsync("Save/del.sav", default).AsTask();
            yield return WaitForTask(deleteMiss);
            Assert.Pass("重复删除幂等成功（不抛异常）");
        }

        [UnityTest]
        public IEnumerator Enumerate_PrefixPushedDown_KeysStrippedAndVerified()
        {
            SeedEntry(TENANT_PREFIX + "Save/a.sav", 10, 11);
            SeedEntry(TENANT_PREFIX + "Save/b.sav", 20, 12);
            SeedEntry(TENANT_PREFIX + "Other/c.sav", 30, 13);

            Task<CloudKvEntryInfo[]> task = _store.EnumerateAsync("Save/", default).AsTask();
            yield return WaitForTask(task);

            Assert.AreEqual(TENANT_PREFIX + "Save/", _handler.LastListQueryPrefix, "前缀应下推到服务端（含租户前缀）");
            CloudKvEntryInfo[] infos = task.Result;
            Assert.AreEqual(2, infos.Length, "仅前缀命中条目返回");
            for (int i = 0; i < infos.Length; i++)
            {
                StringAssert.StartsWith("Save/", infos[i].Key, "返回键应剥离租户前缀");
                Assert.IsTrue(infos[i].Version > 0L, "条目应携带远端修订号");
                Assert.AreEqual(DateTimeKind.Utc, infos[i].LastWriteTimeUtc.Kind, "时间戳应为 UTC");
            }
        }

        [UnityTest]
        public IEnumerator AuthHeader_StaticValue_ThenProviderPrecedence()
        {
            Task<bool> first = _store.ExistsAsync("Save/x.sav", default).AsTask();
            yield return WaitForTask(first);
            Assert.AreEqual("static-token", _handler.LastAuthValue, "未注入提供方时携带静态值");

            _store.AuthTokenProvider = () => "dynamic-jwt";
            Task<bool> second = _store.ExistsAsync("Save/x.sav", default).AsTask();
            yield return WaitForTask(second);
            Assert.AreEqual("dynamic-jwt", _handler.LastAuthValue, "动态令牌提供方应优先于静态值");
        }

        [UnityTest]
        public IEnumerator ServerError_ThrowsRemoteFailure()
        {
            _handler.InjectedStatusCode = 500;
            Task<CloudKvEntry?> task = _store.ReadAsync("Save/a.sav", default).AsTask();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsFaulted, "远端 500 应抛异常（远端失败语义——由后端归一离线降级）");
            Assert.IsInstanceOf<System.IO.IOException>(task.Exception?.InnerException, "非约定状态码归一为 IOException");
        }

        [UnityTest]
        public IEnumerator Timeout_ThrowsTimeoutException()
        {
            _store.TimeoutSeconds = 1;
            _handler.InjectedDelay = TimeSpan.FromSeconds(3);

            Task<long> task = _store.WriteAsync("Save/slow.sav", new byte[] { 1 }, default).AsTask();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsFaulted, "超时应以失败完成");
            Assert.IsInstanceOf<TimeoutException>(task.Exception?.InnerException, "超时应归一为 TimeoutException（远端失败语义）");
        }

        [UnityTest]
        public IEnumerator RevisionFromETag_WhenCustomHeaderAbsent()
        {
            _handler.RevisionViaETag = true;

            Task<long> writeTask = _store.WriteAsync("Save/etag.sav", new byte[] { 7 }, default).AsTask();
            yield return WaitForTask(writeTask);
            Assert.AreEqual(1L, writeTask.Result, "ETag 回退通道应解析修订号");

            Task<CloudKvEntry?> readTask = _store.ReadAsync("Save/etag.sav", default).AsTask();
            yield return WaitForTask(readTask);
            Assert.AreEqual(1L, readTask.Result?.Version, "读回 ETag 修订号");
        }

        /// <summary>
        /// 直种远端条目（枚举/读路径前置数据）。
        /// </summary>
        private void SeedEntry(string key, int byteCount, long revision)
        {
            _handler.Entries[key] = new FakeRestMessageHandler.Entry
            {
                Bytes = new byte[byteCount],
                Revision = revision,
                ModifiedUtc = DateTime.UtcNow + _handler.ClockOffset,
            };
            if (revision > _handler.NextRevision)
            {
                _handler.NextRevision = revision;
            }
        }
    }
}
