using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// JSON 序列化基准。
    /// <para>① 序列化器核心对比（DefaultJson string/bytes vs Newtonsoft vs Unity JsonUtility 参考）；</para>
    /// <para>② JsonHandler 中间件层：经 <see cref="Moirai.Atropos.AssemblyUtility.GetRuntimeTypes"/> 自动发现全部
    /// <see cref="Moirai.Atropos.JsonHandler"/> 实现，经 Activator.CreateInstance
    /// 实例化（与 GameAppSettings 配置流同链路）——新增 handler 实现无需修改本基准；</para>
    /// <para>③ IBufferJsonHandler 能力矩阵。全部数据程序化构建（零外部文件依赖），结束后恢复外观并清理临时状态。</para>
    /// 菜单：Window/Moirai/JSON Benchmark。逐场景自适迭代（每测量段约 150ms），场景间让出主线程保持编辑器响应。
    /// </summary>
    public static class JsonUtilityBenchmark
    {
        private const string TAG = "[JSON-BENCH]";
        private const int WARMUP_MS = 30;
        private const int MEASURE_MS = 150;

        #region 入口 [ENTRY]

        [MenuItem("Window/Moirai/JSON Benchmark")]
        public static void RunFromMenu() => _ = RunAsync();

        #endregion

        #region 场景数据 [DTOs]

        // ===== DTO（含双重编码嵌入 JSON、CJK 富文本、嵌套结构，程序化构建） =====

        [Serializable] public class InventoryDbDto
        {
            public bool autoSave;
            public bool useAdvancedStats;
            public List<ItemDto> items;
            public SettingsDto settings;
        }

        [Serializable] public class ItemDto
        {
            public ItemInfoDto m_Info;
            public string m_ParentId;
            public string m_CategoryId;
            public ExprDto m_CountPerStack;
            public ExprDto m_MaxStacks;
            public string m_Weight;
            public string m_Value;
            public List<PluginDto> plugins;
            public int count;
            public string customName;
            public string InstanceId;
        }

        [Serializable] public class ItemInfoDto
        {
            public string m_ID;
            public bool m_AutoGenId;
            public string m_Title;
            public ImageDto m_Image;
            public BenchColor m_Color;
            public List<string> m_Tags;
            public bool m_Hidden;
        }

        [Serializable] public struct BenchColor { public float r, g, b, a; }
        [Serializable] public struct BenchVec3 { public float x, y, z; }

        [Serializable] public class ImageDto
        {
            public string m_PackageType;
            public string m_Path;
            public string m_Guid;
        }

        [Serializable] public class ExprDto
        {
            public string valueExpression;
            public float m_ValueInit;
            public bool m_Initialized;
        }

        [Serializable] public class PluginDto
        {
            public string m_ID;
            public string m_SerializationNamespace;
            public string m_SerializationType;
            public string m_SerializationData; // 双重编码的嵌入 JSON 字符串
        }

        [Serializable] public class SettingsDto
        {
            public string m_PickupPrompt;
            public string m_DropPrompt;
            public string m_SellPrompt;
            public BenchVec3 m_ColliderSize;
            public float m_ColliderRadius;
            public string m_EquipPrefabLocation;
            public string m_ItemAddedFormat;
            public string m_ItemDroppedFormat;
            public string m_ItemEquippedFormat;
            public string m_ItemUnattachedDestroyedFormat;
            public List<string> m_CustomMessages;
        }

        // ===== 合成场景 DTO =====

        [Serializable] private class SaveDto
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
        [Serializable] private class MixedRoot
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

        /// <summary>结构化测量结果（供结果窗口渲染）。</summary>
        internal sealed class BenchRow
        {
            public string Scenario;
            public string Operation;          // 序列化 / 反序列化
            public double DjString = double.NaN;
            public double DjBytes = double.NaN;
            public double Newtonsoft = double.NaN;
            public double UnityJson = double.NaN; // NaN = 不适用

            /// <summary>该行可用值中的最小值（用于高亮最快项）。</summary>
            public double Best()
            {
                double best = double.MaxValue;
                foreach (double v in new[] { DjString, DjBytes, Newtonsoft, UnityJson })
                {
                    if (!double.IsNaN(v) && v < best) best = v;
                }

                return best;
            }
        }

        private static async Task RunAsync()
        {
            var results = new List<string> { $"{TAG} ===== Json 序列化基准（µs/次，越小越快；UJ=Unity JsonUtility 参考）=====" };
            var rows = new List<BenchRow>();
            var summary = new List<string>();
            JsonHandler originalHandler = null;
            try
            {
                originalHandler = JsonUtility.Handler; // 保存现场，结束恢复
                await RunScenarios(results, rows);
                await RunHandlerMiddleware(results, summary);
                RunCapabilityMatrix(results, summary);
            }
            catch (Exception e)
            {
                results.Add($"{TAG} 异常中止: {e}");
                summary.Add($"异常中止: {e.Message}");
            }
            finally
            {
                Cleanup(originalHandler);
            }

            foreach (var line in results) Debug.Log(line);
            Debug.Log($"{TAG} ===== 基准完成（共 {results.Count - 1} 行）=====");

            // 弹窗展示对比结果（主线程上安全打开）
            BenchmarkResultWindow.Show(rows, summary, string.Join("\n", results));
        }

        /// <summary>测试后清理：恢复外观 handler（触发其 OnInit 重置静态状态）、关闭进度条、释放无用资产。</summary>
        private static void Cleanup(JsonHandler originalHandler)
        {
            if (originalHandler != null && !ReferenceEquals(JsonUtility.Handler, originalHandler))
            {
                JsonUtility.Handler = originalHandler;
            }

            EditorUtility.ClearProgressBar();
            Resources.UnloadUnusedAssets(); // 释放测量期间产生的大量临时对象图
        }

        private static async Task RunScenarios(List<string> results, List<BenchRow> rows)
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

            int done = 0;
            foreach (var (name, payload, ujOk) in scenarios)
            {
                EditorUtility.DisplayProgressBar("Json Benchmark", name, (float)done / scenarios.Count);

                // 输入预生成（保证反序列化输入一致且已就绪）
                string djJson = DefaultJson.ToJson(payload);
                byte[] djBytes = Encoding.UTF8.GetBytes(djJson);
                string nsJson = JsonConvert.SerializeObject(payload);

                // 正确性抽样
                var check = DefaultJson.FromJson(djBytes, payload.GetType());
                results.Add($"{TAG} {name} | 文档: DJ={djBytes.Length}B NS={Encoding.UTF8.GetByteCount(nsJson)}B | 往返抽样={(check != null ? "OK" : "null")}");

                var serRow = new BenchRow { Scenario = name, Operation = "序列化" };
                results.Add(MeasureRow("  序列化",
                    () => DefaultJson.ToJson(payload),
                    () => DefaultJson.ToJsonBytes(payload),
                    () => JsonConvert.SerializeObject(payload),
                    ujOk ? () => UnityEngine.JsonUtility.ToJson(payload) : null,
                    serRow));
                rows.Add(serRow);

                var deserRow = new BenchRow { Scenario = name, Operation = "反序列化" };
                results.Add(MeasureRow("  反序列化",
                    () => DefaultJson.FromJson(djJson, payload.GetType()),
                    () => DefaultJson.FromJson(djBytes, payload.GetType()),
                    () => JsonConvert.DeserializeObject(nsJson, payload.GetType()),
                    ujOk ? () => UnityEngine.JsonUtility.FromJson(djJson, payload.GetType()) : null,
                    deserRow));
                rows.Add(deserRow);

                done++;
                await Task.Delay(1); // 让出主线程
            }
        }

        #region Handler 自动发现 [HANDLER DISCOVERY]

        /// <summary>
        /// 发现全部 <see cref="Moirai.Atropos.JsonHandler"/> 实现（排除抽象/测试程序集），
        /// 经 <see cref="Moirai.Atropos.ReflectionUtility.ResolveImplType{T}"/> 实例化（与 GameAppSettings 配置流同链路）。
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
        private static async Task RunHandlerMiddleware(List<string> results, List<string> summary)
        {
            results.Add($"{TAG} ----- JsonHandler 中间件层（外观 JsonUtility 端到端，handler 自动发现）-----");
            summary.Add("----- JsonHandler 中间件层 -----");

            var handlers = DiscoverHandlers(results);
            if (handlers.Count == 0)
            {
                results.Add($"{TAG} 未发现任何 JsonHandler 实现");
                return;
            }

            results.Add($"{TAG} 发现 {handlers.Count} 个实现: {string.Join(", ", Enumerable.Select(handlers, h => h.name))}");

            var payload = BuildMixed();
            Type payloadType = payload.GetType();

            foreach (var (name, handler) in handlers)
            {
                EditorUtility.DisplayProgressBar("JSON Benchmark", "Handler: " + name, 0.9f);

                try
                {
                    string comboLine = await MeasureHandlerCombo(name, handler, payload, payloadType);
                    results.Add(comboLine);
                    summary.Add(comboLine.Substring(TAG.Length + 1)); // 弹窗摘要行（去前缀）
                }
                catch (Exception e)
                {
                    results.Add($"{TAG} {name} [测量失败: {e.Message}]");
                }
            }
        }

        private static async Task<string> MeasureHandlerCombo(string name, JsonHandler handler, object payload, Type payloadType)
        {
            JsonUtility.Handler = handler;

            string json = JsonUtility.ToJson(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            double serStr = Measure(() => JsonUtility.ToJson(payload));
            double deserStr = Measure(() => JsonUtility.ToObject(payloadType, json));
            double serBytes = Measure(() => JsonUtility.ToJsonBytes(payload));
            double deserBytes = Measure(() => JsonUtility.ToObject(payloadType, bytes));

            bool isBuffer = handler is IBufferJsonHandler;
            string bufferNote = isBuffer ? "原生字节" : "外观回退";
            await Task.Delay(1);
            return $"{TAG} {name} [IBuffer={(isBuffer ? "是" : "否")}] | string {serStr,7:F1}/{deserStr,7:F1} | bytes({bufferNote}) {serBytes,7:F1}/{deserBytes,7:F1} (序列化/反序列化 µs/op)";
        }

        /// <summary>能力矩阵：各实现（自动发现）对字节通路的实际行为验证。</summary>
        private static void RunCapabilityMatrix(List<string> results, List<string> summary)
        {
            results.Add($"{TAG} ----- IBufferJsonHandler 能力矩阵（自动发现）-----");
            summary.Add("----- IBufferJsonHandler 能力矩阵 -----");

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
                    summary.Add($"{name}: 能力验证失败 ({e.Message})");
                }
            }
        }

        #endregion

        #region 测量 [MEASUREMENT]

        private static string MeasureRow(string op, Action djString, Action djBytes, Action newtonsoft, Action unityJson, BenchRow row = null)
        {
            double s = Measure(djString);
            double b = Measure(djBytes);
            double n = Measure(newtonsoft);
            double u = unityJson != null ? Measure(unityJson) : double.NaN;
            string uCell = !double.IsNaN(u) && u >= 0 ? $"{u,8:F1}" : $"{new string('—', 6),8}";

            if (row != null)
            {
                row.DjString = s;
                row.DjBytes = b;
                row.Newtonsoft = n;
                row.UnityJson = u;
            }

            return $"{TAG} {op} | DJ-string {s,8:F1} | DJ-bytes {b,8:F1} | Newtonsoft {n,8:F1} | UJ {uCell} (µs/op)";
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
                    m_PickupPrompt = "拾取",
                    m_DropPrompt = "丢弃",
                    m_SellPrompt = "出售",
                    m_ColliderSize = new BenchVec3 { x = 1, y = 1, z = 1 },
                    m_ColliderRadius = 0.5f,
                    m_EquipPrefabLocation = "Resources/Inventory/Equip",
                    m_ItemAddedFormat = "<color={targetColor}>{targetDisplayName}</color>获得了<color={itemRarityColor}>{itemDisplayName}</color> x{count}。",
                    m_ItemDroppedFormat = "<color={targetColor}>{targetDisplayName}</color>丢弃了<color={itemRarityColor}>{itemDisplayName}</color> x{count}。",
                    m_ItemEquippedFormat = "<color={targetColor}>{targetDisplayName}</color>装备了<color={itemRarityColor}>{itemDisplayName}</color>。",
                    m_ItemUnattachedDestroyedFormat = "<color={targetColor}>{targetDisplayName}</color>从<color={itemRarityColor}>{itemDisplayName}</color>拆下并<b>损毁了</b>了<color={attachmentRarityColor}>{attachmentDisplayName}</color>。",
                    m_CustomMessages = new List<string>(),
                },
            };

            var pluginTypes = new[] { "Moirai.Clotho.Stats.StatModifierPlugin", "Moirai.Clotho.Inventory.EquipAction" };
            for (int i = 0; i < itemCount; i++)
            {
                // 双重编码的嵌入 JSON（还原真实文件里 m_SerializationData 的形态）
                string embedded = i % 2 == 0
                    ? "{\"m_ApplyToRemote\":false,\"m_EquipSlots\":\"Any\",\"m_EquipSlotIds\":[],\"m_StatModifiers\":[{\"m_AffectsStatId\":\"test\",\"m_Applies\":\"Immediately\",\"m_ChangeType\":\"Add\",\"m_Value\":{\"m_Value\":\"0\",\"m_RandomMax\":\"1\",\"initialized\":false},\"InstanceId\":\"ffad862f5fd34ba18f03600f4247f285\"}],\"Title\":\"Stat Modifier\",\"Description\":\"在装备或消耗道具时修改属性。\"}"
                    : "{\"m_AutoEquip\":\"Never\",\"m_SlotIds\":[],\"m_SpawnItem\":false,\"m_EquipSpawn\":{\"m_Parent\":true,\"m_Offset\":{\"x\":0,\"y\":0,\"z\":0},\"m_Rotation\":{\"x\":0,\"y\":0,\"z\":0}},\"_appliedModifiers\":[]}";

                db.items.Add(new ItemDto
                {
                    m_Info = new ItemInfoDto
                    {
                        m_ID = (10001 + i).ToString(),
                        m_AutoGenId = true,
                        m_Title = (10001 + i).ToString(),
                        m_Image = new ImageDto { m_PackageType = "Common", m_Path = "", m_Guid = Guid.NewGuid().ToString("N") },
                        m_Color = new BenchColor { r = 1, g = 1, b = 1, a = 1 },
                        m_Tags = new List<string>(),
                        m_Hidden = false,
                    },
                    m_ParentId = "",
                    m_CategoryId = "",
                    m_CountPerStack = new ExprDto { valueExpression = "0", m_ValueInit = 0, m_Initialized = false },
                    m_MaxStacks = new ExprDto { valueExpression = "0", m_ValueInit = 0, m_Initialized = false },
                    m_Weight = "0",
                    m_Value = "0",
                    plugins = new List<PluginDto>
                    {
                        new PluginDto
                        {
                            m_ID = "",
                            m_SerializationNamespace = "Moirai.Clotho",
                            m_SerializationType = pluginTypes[i % 2],
                            m_SerializationData = embedded,
                        },
                    },
                    count = 0,
                    customName = "",
                    InstanceId = Guid.NewGuid().ToString("N"),
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

    /// <summary>
    /// 基准结果对比窗口：表格化核心场景（最快项高亮 + 相对倍率），下方为 handler 中间件层与能力矩阵摘要。
    /// </summary>
    internal sealed class BenchmarkResultWindow : EditorWindow
    {
        private List<JsonUtilityBenchmark.BenchRow> _rows;
        private List<string> _summary;
        private string _rawLog;
        private Vector2 _tableScroll;
        private Vector2 _summaryScroll;

        private static readonly (string label, Func<JsonUtilityBenchmark.BenchRow, double> get)[] Columns =
        {
            ("DefaultJson-string", r => r.DjString),
            ("DefaultJson-bytes", r => r.DjBytes),
            ("Newtonsoft", r => r.Newtonsoft),
            ("UnityJson(参考)", r => r.UnityJson),
        };

        public static void Show(List<JsonUtilityBenchmark.BenchRow> rows, List<string> summary, string rawLog)
        {
            var window = GetWindow<BenchmarkResultWindow>("JSON Benchmark 对比");
            window._rows = rows;
            window._summary = summary;
            window._rawLog = rawLog;
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            if (_rows == null)
            {
                EditorGUILayout.HelpBox("无结果。请先运行 Window/Moirai/JSON Benchmark。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("核心场景对比（µs/次，绿色 = 该行最快；括号 = 相对最快的倍率）", EditorStyles.boldLabel);

            DrawTable();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("中间件层与能力矩阵", EditorStyles.boldLabel);
            _summaryScroll = EditorGUILayout.BeginScrollView(_summaryScroll, GUILayout.Height(150));
            foreach (string line in _summary)
            {
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制完整日志", GUILayout.Width(140)))
                {
                    EditorGUIUtility.systemCopyBuffer = _rawLog;
                    ShowNotification(new GUIContent("已复制到剪贴板"));
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"共 {_rows.Count} 行测量 · {_summary.Count} 条摘要", EditorStyles.miniLabel);
            }
        }

        private void DrawTable()
        {
            using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
            {
                EditorGUILayout.LabelField("场景", GUILayout.Width(220));
                EditorGUILayout.LabelField("操作", GUILayout.Width(52));
                foreach (var (label, _) in Columns)
                {
                    EditorGUILayout.LabelField(label, GUILayout.Width(110));
                }

                EditorGUILayout.LabelField("最快", GUILayout.Width(80));
            }

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll);
            foreach (var row in _rows)
            {
                double best = row.Best();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(row.Scenario, GUILayout.Width(220));
                    EditorGUILayout.LabelField(row.Operation, GUILayout.Width(52));

                    string winner = null;
                    foreach (var (label, get) in Columns)
                    {
                        double v = get(row);
                        bool isBest = !double.IsNaN(v) && Math.Abs(v - best) < 0.0001;
                        string text = double.IsNaN(v) ? "—" : $"{v:F1}" + (isBest && best > 0 ? $" ({v / best:F1}×)" : string.Empty);
                        var style = isBest ? GreenLabel() : EditorStyles.label;
                        EditorGUILayout.LabelField(text, style, GUILayout.Width(110));
                        if (isBest) winner = label;
                    }

                    EditorGUILayout.LabelField(winner ?? "—", winner != null ? GreenLabel() : EditorStyles.label, GUILayout.Width(80));
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static GUIStyle _greenLabel;

        private static GUIStyle GreenLabel()
        {
            if (_greenLabel == null)
            {
                _greenLabel = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                _greenLabel.normal.textColor = new Color32(0x2E, 0x92, 0x19, 0xFF);
            }

            return _greenLabel;
        }
    }
}
