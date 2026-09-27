using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Moirai.Atropos;
using Moirai.Atropos.Debugger;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace Utility
{
    /// <summary>
    /// JSON 序列化基准（<c>[Explicit]</c>——不参与常规回归，按名手动执行）。
    /// <para>① 序列化器核心对比（DefaultJson string/bytes vs Newtonsoft vs Unity JsonUtility 参考）；</para>
    /// <para>② JsonHandler 中间件层：经 <see cref="AssemblyUtility.GetRuntimeTypes"/> 自动发现全部
    /// <see cref="JsonHandler"/> 实现，经与 GameAppSettings 配置流同链路实例化——新增 handler 实现无需修改本基准；</para>
    /// <para>③ IBufferJsonHandler 能力矩阵。全部数据程序化构建（零外部文件依赖），结束后恢复外观并清理临时状态；
    /// 逐场景自适迭代（每测量段约 150ms），结果经 <see cref="BenchmarkReport"/> 落统一文件夹
    /// &lt;工程根&gt;/Benchmarks/jsonutility-benchmark.xml。原为 Editor 菜单工具（Window/Moirai/JSON Benchmark
    /// + 结果窗），按基准归一裁定迁入 Tests、去交互壳。</para>
    /// </summary>
    [TestFixture]
    [Explicit]
    public sealed class JsonUtilityBenchmark
    {
        private const string TAG = "[JSON-BENCH]";
        private const int WARMUP_MS = 30;
        private const int MEASURE_MS = 150;

        private BenchmarkReport _report;

        #region 场景数据 [DTOs]

        // ===== DTO（含双重编码嵌入 JSON、CJK 富文本、嵌套结构，程序化构建） =====

        [Serializable]
        public class InventoryDbDto
        {
            public bool autoSave;
            public bool useAdvancedStats;
            public List<ItemDto> items;
            public SettingsDto settings;
        }

        [Serializable]
        public class ItemDto
        {
            public ItemInfoDto info;
            public string parentId;
            public string categoryId;
            public ExprDto countPerStack;
            public ExprDto maxStacks;
            public string weight;
            public string value;
            public List<PluginDto> plugins;
            public int count;
            public string customName;
            public string instanceId;
        }

        [Serializable]
        public class ItemInfoDto
        {
            public string id;
            public bool autoGenId;
            public string title;
            public ImageDto image;
            public BenchColor color;
            public List<string> tags;
            public bool hidden;
        }

        [Serializable] public struct BenchColor { public float r, g, b, a; }
        [Serializable] public struct BenchVec3 { public float x, y, z; }

        [Serializable]
        public class ImageDto
        {
            public string packageType;
            public string path;
            public string guid;
        }

        [Serializable]
        public class ExprDto
        {
            public string valueExpression;
            public float valueInit;
            public bool initialized;
        }

        [Serializable]
        public class PluginDto
        {
            public string id;
            public string serializationNamespace;
            public string serializationType;
            public string serializationData; // 双重编码的嵌入 JSON 字符串
        }

        [Serializable]
        public class SettingsDto
        {
            public string pickupPrompt;
            public string dropPrompt;
            public string sellPrompt;
            public BenchVec3 colliderSize;
            public float colliderRadius;
            public string equipPrefabLocation;
            public string itemAddedFormat;
            public string itemDroppedFormat;
            public string itemEquippedFormat;
            public string itemUnattachedDestroyedFormat;
            public List<string> customMessages;
        }

        // ===== 合成场景 DTO =====

        [Serializable]
        private class SaveDto
        {
            public string playerName;
            public int level;
            public float exp;
            public bool hardcore;
            public List<int> unlockedChapters = new List<int>();
            public SavePos lastPos = new SavePos();
        }

        [Serializable] private class SavePos { public float x, y, z; }

        [Serializable] private class MixedItem { public int id; public string name; public List<int> tags; }
        [Serializable]
        private class MixedRoot
        {
            public List<MixedItem> items;
            public Dictionary<string, int> counts;
            public int[] scores;
            public float[] weights;
            public float ratio;
        }

        [Serializable] private class IntArrayHolder { public int[] values; }
        [Serializable] private class FloatArrayHolder { public float[] values; }
        [Serializable] private class IntListHolder { public List<int> values; }
        [Serializable] private class DictHolder { public Dictionary<string, int> values; }
        [Serializable] private class Vec3Holder { public BenchVec3[] values; }
        [Serializable] private class StringsHolder { public List<string> values; }
        [Serializable] private class ChainNode { public string id; public ChainNode child; }

        #endregion

        #region 主流程 [MAIN FLOW]

        [Test]
        public void RunMatrix_MeasuresAndExportsXml()
        {
            _report = new BenchmarkReport("JsonUtility");
            _report.SetMetadata("measureMs", MEASURE_MS.ToString());

            var results = new List<string> { $"{TAG} ===== Json 序列化基准（µs/次，越小越快；UJ=Unity JsonUtility 参考）=====" };
            JsonHandler originalHandler = null;
            long wallStart = Stopwatch.GetTimestamp();
            try
            {
                originalHandler = JsonUtility.Handler; // 保存现场，结束恢复
                RunScenarios(results);
                RunHandlerMiddleware(results);
                RunCapabilityMatrix(results);
            }
            catch (Exception e)
            {
                results.Add($"{TAG} 异常中止: {e}");
                _report.SetMetadata("aborted", e.Message);
            }
            finally
            {
                Cleanup(originalHandler);
            }

            foreach (var line in results) Debug.Log(line);
            Debug.Log($"{TAG} ===== 基准完成（共 {results.Count - 1} 行）=====");

            _report.TotalMs = (Stopwatch.GetTimestamp() - wallStart) * 1000.0 / Stopwatch.Frequency;
            _report.WriteXml(_report.ResolveXmlPath());
        }

        /// <summary>结束后清理：恢复外观 handler（触发其 OnInit 重置静态状态）、释放无用资产。</summary>
        private void Cleanup(JsonHandler originalHandler)
        {
            if (originalHandler != null && !ReferenceEquals(JsonUtility.Handler, originalHandler))
            {
                JsonUtility.Handler = originalHandler;
            }

            UnityEngine.Resources.UnloadUnusedAssets(); // 释放测量期间产生的大量临时对象图
        }

        private void RunScenarios(List<string> results)
        {
            // 场景装配（统一根对象，保证各库文档一致；全部程序化构建）
            var scenarios = new List<(string name, object payload, bool unityJsonSupported)>
            {
                ("DTO(含嵌入Json+CJK)", BuildInventoryDb(64), true),
                ("小存档DTO", BuildSaveDto(), true),
                ("混合图(50项×10tag+100字典+数组)", BuildMixed(), false),
                ("int[5000]", new IntArrayHolder { values = BuildInts(5000) }, true),
                ("float[5000]", new FloatArrayHolder { values = BuildFloats(5000) }, true),
                ("List<int>(5000)", new IntListHolder { values = new List<int>(BuildInts(5000)) }, true),
                ("Dict<string,int>(1000)", new DictHolder { values = BuildDict(1000) }, false),
                ("Vec3结构体[1000]", new Vec3Holder { values = BuildVec3s(1000) }, true),
                ("深链32层", BuildChain(32), true),
                ("字符串集(500条 CJK+转义)", new StringsHolder { values = BuildStrings(500) }, true),
            };

            foreach (var (name, payload, ujOk) in scenarios)
            {
                // 输入预生成（保证反序列化输入一致且已就绪）
                string djJson = DefaultJson.ToJson(payload);
                byte[] djBytes = Encoding.UTF8.GetBytes(djJson);
                string nsJson = JsonConvert.SerializeObject(payload);

                // 正确性抽样
                var check = DefaultJson.FromJson(djBytes, payload.GetType());
                results.Add($"{TAG} {name} | 文档: DJ={djBytes.Length}B NS={Encoding.UTF8.GetByteCount(nsJson)}B | 往返抽样={(check != null ? "OK" : "null")}");

                MeasureRow(name + " 序列化",
                    () => DefaultJson.ToJson(payload),
                    () => DefaultJson.ToJsonBytes(payload),
                    () => JsonConvert.SerializeObject(payload),
                    ujOk ? () => UnityEngine.JsonUtility.ToJson(payload) : null,
                    results);

                MeasureRow(name + " 反序列化",
                    () => DefaultJson.FromJson(djJson, payload.GetType()),
                    () => DefaultJson.FromJson(djBytes, payload.GetType()),
                    () => JsonConvert.DeserializeObject(nsJson, payload.GetType()),
                    ujOk ? () => UnityEngine.JsonUtility.FromJson(djJson, payload.GetType()) : null,
                    results);
            }
        }

        #endregion

        #region Handler 自动发现 [HANDLER DISCOVERY]

        /// <summary>
        /// 发现全部 <see cref="JsonHandler"/> 实现（排除抽象/测试程序集），
        /// 经 <see cref="ReflectionUtility.ResolveImplType{T}"/> 实例化（与 GameAppSettings 配置流同链路）。
        /// 单个 handler 实例化失败仅记录，不中断整体。
        /// </summary>
        private static List<(string name, JsonHandler handler)> DiscoverHandlers(List<string> results)
        {
            var discovered = new List<(string, JsonHandler)>();
            List<Type> handlerTypes;
            try
            {
                handlerTypes = AssemblyUtility.GetRuntimeTypes(typeof(JsonHandler));
            }
            catch (Exception e)
            {
                results.Add($"{TAG} Handler 发现失败: {e.Message}");
                return discovered;
            }

            foreach (Type type in handlerTypes)
            {
                try
                {
                    // ResolveImplType 走 AssemblyUtility.GetType + Activator（GameAppSettings 同款解析链路）
                    JsonHandler handler = null;
                    ReflectionUtility.ResolveImplType(ref handler, type.FullName, typeof(DefaultJsonHandler));
                    discovered.Add((type.Name, handler));
                }
                catch (Exception e)
                {
                    results.Add($"{TAG} 跳过 {type.FullName}: 实例化失败 ({e.Message})");
                }
            }

            return discovered;
        }

        #endregion

        /// <summary>JsonHandler 中间件层：外观挂各实现（自动发现）的端到端开销（含抽象层与异常包装成本）。</summary>
        private void RunHandlerMiddleware(List<string> results)
        {
            results.Add($"{TAG} ----- JsonHandler 中间件层（外观 JsonUtility 端到端，handler 自动发现）-----");

            var handlers = DiscoverHandlers(results);
            if (handlers.Count == 0)
            {
                results.Add($"{TAG} 未发现任何 JsonHandler 实现");
                return;
            }

            results.Add($"{TAG} 发现 {handlers.Count} 个实现: {string.Join(", ", handlers.Select(h => h.name))}");

            var payload = BuildMixed();
            Type payloadType = payload.GetType();

            foreach (var (name, handler) in handlers)
            {
                try
                {
                    JsonUtility.Handler = handler;

                    // 预热
                    JsonUtility.ToJson(payload);

                    var caseResult = _report.Add(new BenchmarkCaseResult
                    {
                        Name = "Handler " + name,
                        Category = "Middleware",
                        Trials = 1,
                    });

                    double serStr = Measure(() => JsonUtility.ToJson(payload));
                    double deserStr = Measure(() => JsonUtility.ToObject(payloadType, JsonUtility.ToJson(payload)));
                    double serBytes = Measure(() => JsonUtility.ToJsonBytes(payload));
                    double deserBytes = Measure(() => JsonUtility.ToObject(payloadType, JsonUtility.ToJsonBytes(payload)));

                    bool isBuffer = handler is IBufferJsonHandler;
                    caseResult.Metric("IBuffer", isBuffer ? "native" : "fallback")
                        .Metric("serStrUs", serStr.ToString("F1"))
                        .Metric("deserStrUs", deserStr.ToString("F1"))
                        .Metric("serBytesUs", serBytes.ToString("F1"))
                        .Metric("deserBytesUs", deserBytes.ToString("F1"));

                    string bufferNote = isBuffer ? "原生字节" : "外观回退";
                    results.Add($"{TAG} {name} [IBuffer={(isBuffer ? "是" : "否")}] | string {serStr,7:F1}/{deserStr,7:F1} | bytes({bufferNote}) {serBytes,7:F1}/{deserBytes,7:F1} (序列化/反序列化 µs/op)");
                }
                catch (Exception e)
                {
                    results.Add($"{TAG} {name} [测量失败: {e.Message}]");
                }
            }
        }

        /// <summary>能力矩阵：各实现（自动发现）对字节通路的实际行为验证（非计时，仅日志与结果抽样）。</summary>
        private static void RunCapabilityMatrix(List<string> results)
        {
            results.Add($"{TAG} ----- IBufferJsonHandler 能力矩阵（自动发现）-----");

            var handlers = DiscoverHandlers(results);
            var payload = new Vec3Holder { values = BuildVec3s(100) };

            foreach (var (name, handler) in handlers)
            {
                try
                {
                    JsonUtility.Handler = handler;
                    byte[] bytesOut = JsonUtility.ToJsonBytes(payload);
                    var back = (Vec3Holder)JsonUtility.ToObject(typeof(Vec3Holder), bytesOut);
                    bool isBuffer = handler is IBufferJsonHandler;
                    string verdict = back.values != null && back.values.Length == 100 && back.values[0].x.Equals(payload.values[0].x) ? "OK" : "FAIL";
                    results.Add($"{TAG} {name}: IBuffer={(isBuffer ? "是(原生字节)" : "否(外观回退)")} | bytes往返={verdict}");
                }
                catch (Exception e)
                {
                    results.Add($"{TAG} {name}: 能力验证失败 ({e.Message})");
                }
            }
        }

        #region 测量 [MEASUREMENT]

        private void MeasureRow(string caseName, Action djString, Action djBytes, Action newtonsoft, Action unityJson, List<string> results)
        {
            double s = Measure(djString);
            double b = Measure(djBytes);
            double n = Measure(newtonsoft);
            double u = unityJson != null ? Measure(unityJson) : double.NaN;
            string uCell = !double.IsNaN(u) && u >= 0 ? $"{u,8:F1}" : $"{new string('—', 6),8}";

            results.Add($"{TAG} {caseName} | DJ-string {s,8:F1} | DJ-bytes {b,8:F1} | Newtonsoft {n,8:F1} | UJ {uCell} (µs/op)");

            AddOpCase(caseName + "/DJ-string", s);
            AddOpCase(caseName + "/DJ-bytes", b);
            AddOpCase(caseName + "/Newtonsoft", n);
            if (!double.IsNaN(u)) AddOpCase(caseName + "/UnityJson", u);
        }

        /// <summary>单个操作进报告（µs/op → ns/op 口径；自适迭代次数不定，min/mean/max 同值）。</summary>
        private void AddOpCase(string name, double usPerOp)
        {
            _report.Add(new BenchmarkCaseResult
            {
                Name = name,
                Category = "Serializer",
                Trials = 1,
                MinMs = usPerOp / 1000.0,
                MeanMs = usPerOp / 1000.0,
                MaxMs = usPerOp / 1000.0,
                NsPerOp = usPerOp * 1000.0,
            });
        }

        /// <summary>自适迭代测量：预热 ~30ms 后计量 ~150ms，返回 µs/次。</summary>
        private static double Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < WARMUP_MS) action();
            sw.Restart();
            long iters = 0;
            while (sw.ElapsedMilliseconds < MEASURE_MS)
            {
                action();
                iters++;
                if (iters >= 1_000_000) break;
            }

            sw.Stop();
            return sw.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, iters);
        }

        #endregion

        #region 数据构建 [PAYLOAD BUILDERS]

        /// <summary>程序化构建数据（含插件双重编码 JSON 与 CJK 富文本）。</summary>
        private static InventoryDbDto BuildInventoryDb(int itemCount)
        {
            var db = new InventoryDbDto
            {
                autoSave = true,
                useAdvancedStats = true,
                items = new List<ItemDto>(itemCount),
                settings = new SettingsDto
                {
                    pickupPrompt = "拾取",
                    dropPrompt = "丢弃",
                    sellPrompt = "出售",
                    colliderSize = new BenchVec3 { x = 1, y = 1, z = 1 },
                    colliderRadius = 0.5f,
                    equipPrefabLocation = "Resources/Inventory/Equip",
                    itemAddedFormat = "<color={targetColor}>{targetDisplayName}</color>获得了<color={itemRarityColor}>{itemDisplayName}</color> x{count}。",
                    itemDroppedFormat = "<color={targetColor}>{targetDisplayName}</color>丢弃了<color={itemRarityColor}>{itemDisplayName}</color>。",
                    itemEquippedFormat = "<color={targetColor}>{targetDisplayName}</color>装备了<color={itemRarityColor}>{itemDisplayName}</color>。",
                    itemUnattachedDestroyedFormat = "<color={targetColor}>{targetDisplayName}</color>从<color={itemRarityColor}>{itemDisplayName}</color>拆下并<b>损毁了</b>了<color={attachmentRarityColor}>{attachmentDisplayName}</color>。",
                    customMessages = new List<string>(),
                },
            };

            var pluginTypes = new[] { "Moirai.Clotho.Stats.StatModifierPlugin", "Moirai.Clotho.Inventory.EquipAction" };
            for (int i = 0; i < itemCount; i++)
            {
                // 双重编码的嵌入 JSON（还原真实文件里 serializationData 的形态）
                string embedded = i % 2 == 0
                    ? "{\"m_ApplyToRemote\":false,\"m_EquipSlots\":\"Any\",\"m_EquipSlotIds\":[],\"m_StatModifiers\":[{\"m_AffectsStatId\":\"test\",\"m_Applies\":\"Immediately\",\"m_ChangeType\":\"Add\",\"m_Value\":{\"m_Value\":\"0\",\"m_RandomMax\":\"1\",\"initialized\":false},\"InstanceId\":\"ffad862f5fd34ba18f03600f4247f28e\"}],\"Title\":\"Stat Modifier\",\"Description\":\"在装备或消耗道具时修改属性。\"}"
                    : "{\"m_AutoEquip\":\"Never\",\"m_SlotIds\":[],\"m_SpawnItem\":false,\"m_EquipSpawn\":{\"m_Parent\":true,\"m_Offset\":{\"x\":0,\"y\":0,\"z\":0},\"m_Rotation\":{\"x\":0,\"y\":0,\"z\":0}},\"_appliedModifiers\":[]}";

                db.items.Add(new ItemDto
                {
                    info = new ItemInfoDto
                    {
                        id = (10001 + i).ToString(),
                        autoGenId = true,
                        title = (10001 + i).ToString(),
                        image = new ImageDto { packageType = "Common", path = "", guid = Guid.NewGuid().ToString("N") },
                        color = new BenchColor { r = 1, g = 1, b = 1, a = 1 },
                        tags = new List<string>(),
                        hidden = false,
                    },
                    parentId = "",
                    categoryId = "",
                    countPerStack = new ExprDto { valueExpression = "0", valueInit = 0, initialized = false },
                    maxStacks = new ExprDto { valueExpression = "0", valueInit = 0, initialized = false },
                    weight = "0",
                    value = "0",
                    plugins = new List<PluginDto>
                    {
                        new PluginDto
                        {
                            id = "",
                            serializationNamespace = "Moirai.Clotho",
                            serializationType = pluginTypes[i % 2],
                            serializationData = embedded,
                        },
                    },
                    count = 0,
                    customName = "",
                    instanceId = Guid.NewGuid().ToString("N"),
                });
            }

            return db;
        }

        private static SaveDto BuildSaveDto()
        {
            return new SaveDto
            {
                playerName = "玩家_测试",
                level = 42,
                exp = 12345.678f,
                hardcore = true,
                unlockedChapters = new List<int> { 1, 2, 3, 5, 8 },
                lastPos = new SavePos { x = 10.5f, y = -3.25f, z = 88f },
            };
        }

        private static MixedRoot BuildMixed()
        {
            var rnd = new Random(42);
            var root = new MixedRoot
            {
                items = new List<MixedItem>(),
                counts = new Dictionary<string, int>(),
                scores = BuildInts(100),
                weights = BuildFloats(100),
                ratio = 0.75f,
            };
            for (int i = 0; i < 50; i++)
            {
                var tags = new List<int>();
                for (int t = 0; t < 10; t++) tags.Add(rnd.Next(1000));
                root.items.Add(new MixedItem { id = i, name = "item_" + i, tags = tags });
                root.counts["k" + i] = rnd.Next(100);
            }

            return root;
        }

        private static int[] BuildInts(int n)
        {
            var rnd = new Random(7);
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = rnd.Next();
            return a;
        }

        private static float[] BuildFloats(int n)
        {
            var rnd = new Random(11);
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = (float)(rnd.NextDouble() * 100);
            return a;
        }

        private static Dictionary<string, int> BuildDict(int n)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < n; i++) d["key_" + i] = i * 7;
            return d;
        }

        private static BenchVec3[] BuildVec3s(int n)
        {
            var rnd = new Random(13);
            var a = new BenchVec3[n];
            for (int i = 0; i < n; i++) a[i] = new BenchVec3 { x = (float)rnd.NextDouble(), y = (float)rnd.NextDouble(), z = (float)rnd.NextDouble() };
            return a;
        }

        private static ChainNode BuildChain(int depth)
        {
            ChainNode node = null;
            for (int i = 0; i < depth; i++) node = new ChainNode { id = "n" + i, child = node };
            return node;
        }

        private static List<string> BuildStrings(int n)
        {
            var list = new List<string>(n);
            for (int i = 0; i < n; i++)
                list.Add($"物品\"{i}\"<color=#BA3026>稀有</color>\\路径/中文🌍描述\r\n第二行\t{i}");
            return list;
        }

        #endregion
    }
}
