using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// 开窗载荷通道的托管分配基准（L3，<c>[Explicit]</c> 按名手动执行）：六格各量一次「开+关」稳态往返的本线程分配增量。
    /// </summary>
    /// <remarks>
    /// 量具是 <see cref="GC.GetAllocatedBytesForCurrentThread"/>（本线程口径；计划里写的 <c>GetAllocatedMemoryForCurrentThread</c>
    /// 在 Unity 的 .NET 档不存在，仓内既有的同口径计量走的就是这一枚）。 <br />
    /// 口径已知：本包 <c>Documentation~/zh/Testing.md</c> 记录该字节口径在编辑器 Mono、Mono 玩家、IL2CPP 玩家三处实测恒 0 ——
    /// 测不出分配不等于没有分配。判假红预案按派发口径执行：若读数出现 4096 的整数倍噪音，先怀疑量具（并行 build 抢 CPU），
    /// 改引用同一性/计数类结构断言（每格都另带一组同一性与计数断言，正是为此留着），不抬阈值。 <br />
    /// 档位承诺（spec §9.4）：静态腿 struct/class DTO、动态腿 class、无载荷腿、<c>default(ct)</c> 全链稳态增量 0；
    /// 动态腿基元允许每次开窗装箱一次，按实测字节记档。 <br />
    /// 每格先暖机一轮（首轮装载与建面板不计）再跑 32 次往返；探针窗一律走停放档，往返走停放重取那条稳态支路。 <br />
    /// 共享状态走 static、实测字节导出 <see cref="ReportPath"/>（对齐 Kernel/GenericObjectPool 基准范式）。
    /// 线程契约：仅主线程（PlayMode 主线程跑的基准与本服务的线程契约同档）。
    /// </remarks>
    [Explicit]
    [TestFixture]
    public sealed class UIOpenAllocBenchmarkTests
    {
        /// <summary>实测字节导出路径（工程根相对）：与测试桥的状态文件同一目录口径。</summary>
        private const string ReportPath = "Temp/ui-open-alloc-benchmark.txt";

        /// <summary>每格往返次数：够摊平一次性的池与列表扩容，又不把基准跑成分钟级。</summary>
        private const int ITERATIONS = 32;

        /// <summary>动态腿基元的装箱上界（字节/次）：一枚装箱 header+字段按 32 字节预算，超出即红。</summary>
        private const int BOXING_BYTES_PER_ITERATION = 32;

        /// <summary>跨用例累积的读数行：NUnit 每个测试新建 fixture 实例，基准记账走 static（与 KernelBenchmark 同口径）。</summary>
        private static readonly List<string> s_Lines = new List<string>();

        private readonly List<GameObject> _trackedShells = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(UIService.IsValid, "量具前提坏了：播放态框架没把 UI 服务立起来，开窗腿拿不到共享栈");
            Assert.AreEqual(0, UIService.SharedLedger.PeekStack().Count, "量具前提坏了：进门时共享栈上不干净");
        }

        [TearDown]
        public void TearDown()
        {
            UIService.CloseAll(true);

            for (var i = 0; i < _trackedShells.Count; i++)
            {
                if (_trackedShells[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedShells[i]);
                }
            }

            _trackedShells.Clear();
        }

        [OneTimeTearDown]
        public void ExportMeasuredBytes()
        {
            var text = new StringBuilder();
            text.AppendLine("UIService 开窗载荷通道 · 托管分配基准（GC.GetAllocatedBytesForCurrentThread，本线程口径）");
            text.AppendLine("iterations=" + ITERATIONS + "  grid=" + BOXING_BYTES_PER_ITERATION + "B/iter(动态腿基元装箱上界)");
            for (var i = 0; i < s_Lines.Count; i++)
            {
                text.AppendLine(s_Lines[i]);
            }

            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, text.ToString(), Encoding.UTF8);
        }

        #region 六格 [SIX GRIDS]

        /// <summary>格 1：无载荷开+关往返（缓存窗停放重取稳态）——承诺增量 0。</summary>
        [Test]
        public void Grid1_NoPayload_OpenCloseRound_SteadyStateAllocatesZero()
        {
            var delta = Measure("Grid1_NoPayload", "无载荷腿（停放重取稳态）", () =>
            {
                UIService.ShowUI<AllocPlainWindow>("AllocNoPayload");
                UIService.CloseUI<AllocPlainWindow>("AllocNoPayload");
            });

            Assert.Zero(delta, "无载荷往返稳态不得有托管分配");
            AssertIdentityAfterRound<AllocPlainWindow>("AllocNoPayload");
        }

        /// <summary>格 2：静态腿 struct DTO（停放重取稳态）——承诺增量 0（泛型直塞不装箱）。</summary>
        [Test]
        public void Grid2_StaticLegStructDto_SteadyStateAllocatesZero()
        {
            var dto = new AllocDto { Value = 7, Text = "seven" };
            var delta = Measure("Grid2_StaticLegStruct", "静态腿 struct DTO", () =>
            {
                UIService.ShowUI<AllocStructWindow, AllocDto>(in dto, "AllocStruct");
                UIService.CloseUI<AllocStructWindow>("AllocStruct");
            });

            Assert.Zero(delta, "静态腿 struct 载荷经泛型直塞通道不得装箱");
            var window = AssertIdentityAfterRound<AllocStructWindow>("AllocStruct");
            Assert.AreEqual(7, window.Payload.Value, "量具前提坏了：载荷落进了槽");
        }

        /// <summary>格 3：静态腿 class DTO——承诺增量 0（存的是引用本身）。</summary>
        [Test]
        public void Grid3_StaticLegClassDto_SteadyStateAllocatesZero()
        {
            var box = new AllocBox();
            var delta = Measure("Grid3_StaticLegClass", "静态腿 class DTO", () =>
            {
                UIService.ShowUI<AllocClassWindow, AllocBox>(in box, "AllocStaticClass");
                UIService.CloseUI<AllocClassWindow>("AllocStaticClass");
            });

            Assert.Zero(delta, "静态腿 class 载荷只写引用，不复制不装箱");
            var window = AssertIdentityAfterRound<AllocClassWindow>("AllocStaticClass");
            Assert.AreSame(box, window.Payload, "落进槽的是调用方那一枚引用");
        }

        /// <summary>格 4：动态腿 class（<see cref="UIPayload"/> 擦除）——承诺增量 0（引用型只存引用）。</summary>
        [Test]
        public void Grid4_DynamicLegClass_SteadyStateAllocatesZero()
        {
            var box = new AllocBox();
            var delta = Measure("Grid4_DynamicLegClass", "动态腿 class（UIPayload 擦除）", () =>
            {
                UIService.ShowUI(typeof(AllocClassWindow), "AllocDynClass", null, false, UIPayload.From(box));
                UIService.CloseUI<AllocClassWindow>("AllocDynClass");
            });

            Assert.Zero(delta, "动态腿的引用型载荷按口径零分配：擦除载体只存引用");
            var window = AssertIdentityAfterRound<AllocClassWindow>("AllocDynClass");
            Assert.AreSame(box, window.Payload, "擦除后按 TArg 取回的是同一枚引用");
        }

        /// <summary>格 5：动态腿基元（int）——允许每次开窗装箱一次，实测字节记档并按 32 字节/次的上界守。</summary>
        [Test]
        public void Grid5_DynamicLegPrimitive_RecordsBoxingWithinBudget()
        {
            var delta = Measure("Grid5_DynamicLegPrimitive", "动态腿基元 int（允许装箱一次）", () =>
            {
                UIService.ShowUI(typeof(AllocIntWindow), "AllocDynInt", null, false, UIPayload.From(7));
                UIService.CloseUI<AllocIntWindow>("AllocDynInt");
            });

            Assert.LessOrEqual(delta, BOXING_BYTES_PER_ITERATION * ITERATIONS,
                "动态腿基元的装箱预算是每次开窗一枚 int：超出即契约坏了（不抬阈值，先查量具）");
            var window = AssertIdentityAfterRound<AllocIntWindow>("AllocDynInt");
            Assert.AreEqual(7, window.Payload, "量具前提坏了：装箱后按 TArg 取回原值");
        }

        /// <summary>格 6：<c>default(ct)</c> 全链（对照格）——不收令牌的腿不建取消源、不登记，承诺增量 0。</summary>
        [Test]
        public void Grid6_DefaultCancellationToken_FullChainAllocatesZero()
        {
            var delta = Measure("Grid6_DefaultToken", "default(ct) 全链（对照格）", () =>
            {
                UIService.ShowUIAsync<AllocPlainWindow>("AllocNoCt", null, false, default);
                UIService.CloseUI<AllocPlainWindow>("AllocNoCt");
            });

            Assert.Zero(delta, "default(ct) 那一档不得产生任何取消侧分配");
            AssertIdentityAfterRound<AllocPlainWindow>("AllocNoCt");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>先暖机一轮（首轮装载与建面板不计），再量 <paramref name="iterations"/> 次往返的本线程分配增量。</summary>
        private static long Measure(string grid, string label, Action round)
        {
            round();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < ITERATIONS; i++)
            {
                round();
            }

            var delta = GC.GetAllocatedBytesForCurrentThread() - before;
            s_Lines.Add(grid + " | " + label + " | deltaBytes=" +
                        delta.ToString(CultureInfo.InvariantCulture) +
                        " | bytesPerIteration=" + (delta / (double)ITERATIONS).ToString("0.###", CultureInfo.InvariantCulture));
            return delta;
        }

        /// <summary>结构档：往返之后栈上必须仍是同一只实例（量具失效时的真判据，不抬阈值）。</summary>
        private T AssertIdentityAfterRound<T>(string windowName) where T : UGUIWindow, new()
        {
            UIService.ShowUI<T>(windowName);
            var first = UIService.GetWindow<T>(windowName);
            Assert.IsNotNull(first, "量具前提坏了：{0} 这一名要开得起来", windowName);
            Track(first);
            UIService.CloseUI<T>(windowName);
            UIService.ShowUI<T>(windowName);
            var second = UIService.GetWindow<T>(windowName);
            Assert.AreSame(first, second, "停放重取交回的必须是同一只实例");
            return second;
        }

        /// <summary>把窗口此刻的面板本体登记进出门清理表。</summary>
        private void Track(UIWindow window)
        {
            _trackedShells.Add(window?.gameObject);
        }

        /// <summary>代码建出的 uGUI 面板：只带一枚 <see cref="Canvas"/>——<c>BindPanel</c> 认的就是这一枚组件。</summary>
        private static GameObject NewCodeUGUIPanel(string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.SetActive(true);
            panel.transform.SetParent(UIService.UIRoot, false);
            panel.AddComponent<Canvas>();
            return panel;
        }

        /// <summary>基准用的强类型 struct DTO：静态腿按值直达的那一枚。</summary>
        internal struct AllocDto
        {
            internal int Value;
            internal string Text;
        }

        /// <summary>基准用的 class DTO：两腿都只存引用的那一枚。</summary>
        internal sealed class AllocBox
        {
            internal int Value;
        }

        /// <summary>无载荷探针窗（缓存实例）：面板代码建出，装载只跑一次，之后走停放重取稳态。</summary>
        [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
        internal sealed class AllocPlainWindow : UGUIWindow
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        /// <summary>静态腿 struct DTO 探针窗（缓存实例）。</summary>
        [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
        internal sealed class AllocStructWindow : UGUIWindow<AllocDto>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        /// <summary>class DTO 探针窗（缓存实例）：静态腿与动态腿共用这一枚，两格各用各的窗口名。</summary>
        [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
        internal sealed class AllocClassWindow : UGUIWindow<AllocBox>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        /// <summary>动态腿基元探针窗（缓存实例）：<see cref="UIPayload"/> 擦除后按 <c>int</c> 取回，允许装箱一次。</summary>
        [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
        internal sealed class AllocIntWindow : UGUIWindow<int>
        {
            protected internal override bool LoadPanel(string assetLocation, bool fromResources) =>
                BindPanel(NewCodeUGUIPanel(GetType().Name));

            protected internal override UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct) =>
                UniTask.FromResult(LoadPanel(assetLocation, fromResources));
        }

        #endregion
    }
}
