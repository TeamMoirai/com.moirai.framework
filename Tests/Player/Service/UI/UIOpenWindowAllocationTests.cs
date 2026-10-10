using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.UI;
using NUnit.Framework;
using Testing;
using UnityEngine;

namespace Service.UI
{
    /// <summary>
    /// 开窗载荷通道的 0-GC 验收（L3）：六格各量一次「开+关」稳态往返的 <c>GC.Alloc</c> 分配事件数。
    /// </summary>
    /// <remarks>
    /// 量具是 <see cref="AllocationCapture"/>（事件口径，与 UTF 官方 <c>AllocatingGCMemoryConstraint</c> 同机制）——
    /// 本包 <c>Documentation~/zh/Testing.md</c>《0-GC 验收》明令禁用字节前后差：那是编辑器 Mono／Mono 玩家／IL2CPP 玩家实测恒 0 的失明量具。
    /// 事件口径下「0 事件」比「0 字节」更强：任何一次分配都至少计 1 事件。 <br />
    /// 采样收不到的运行时由测量台整组 <see cref="Assert.Ignore"/>，绝不把「测不出分配」当成「没有分配」。 <br />
    /// 档位承诺（spec §9.4）：静态腿 struct/class DTO、动态腿 class、无载荷腿、<c>default(ct)</c> 全链稳态 0 事件；
    /// 动态腿基元每次开窗允许装箱一次，按常数事件预算守（锁「不随载荷规模增长」）。 <br />
    /// 每格先由测量台预热一轮（JIT、池扩容、首轮建面板都不计），再跑 <see cref="ITERATIONS"/> 次往返；
    /// 探针窗一律走停放档（<c>cacheTimeToDestroy: -1</c>），往返命中「停放重取」那条稳态支路。 <br />
    /// 结构档与计量档并列：停放重取必须交回同一实例、class 载荷必须按引用直达——量具哪天坏了，这两条先替它喊。 <br />
    /// 线程契约：仅主线程；实测事件数导出 <see cref="ReportPath"/>（对齐 Audio/Resource/Timer 的 0-GC 格与 Kernel 基准范式）。
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    public sealed class UIOpenWindowAllocationTests
    {
        /// <summary>实测事件数导出路径（工程根相对）：与状态文件同一目录口径。</summary>
        private const string ReportPath = "Temp/ui-open-alloc-benchmark.txt";

        /// <summary>每格往返次数：够摊平一次性的池与列表扩容，又不把基准跑成分钟级。</summary>
        private const int ITERATIONS = 32;

        /// <summary>动态腿基元的装箱事件预算：每次开窗一个 <c>int</c> 装箱 ⇒ 上界就是往返次数。</summary>
        private const int PRIMITIVE_BOXING_EVENTS_PER_ITERATION = 1;

        /// <summary>诊断格（临时）的单侧往返次数：够把「每次 1 事件」与「0 事件」分开即可。</summary>
        private const int DIAG_ITERATIONS = 10;

        /// <summary>跨用例累积的读数行：NUnit 每个测试新建 fixture 实例，基准记账走 static。</summary>
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
        public void ExportMeasuredEvents()
        {
            var text = new StringBuilder();
            text.AppendLine("UIService 开窗载荷通道 · 托管分配验收（GC.Alloc 采样事件数，本线程口径）");
            text.AppendLine("iterations=" + ITERATIONS +
                            "  primitiveBudget=" + (PRIMITIVE_BOXING_EVENTS_PER_ITERATION * ITERATIONS) +
                            "events(动态腿基元装箱上界)");
            for (var i = 0; i < s_Lines.Count; i++)
            {
                text.AppendLine(s_Lines[i]);
            }

            Directory.CreateDirectory("Temp");
            File.WriteAllText(ReportPath, text.ToString(), Encoding.UTF8);
        }

        /// <summary>测量台自身的牙：采样确实推进时，一次必然分配必须被计到——否则整组 0 事件断言都是假绿。</summary>
        [Test]
        public void MeasureManaged_DetectsKnownAllocation()
        {
            AllocationCapture.CalibrateKnownAllocation();
        }

        #region 六格 [SIX GRIDS]

        /// <summary>格 1：无载荷开+关往返（缓存窗停放重取稳态）——承诺 0 事件。</summary>
        [Test]
        public void NoPayload_OpenCloseRound_SteadyStateAllocatesZero()
        {
            var events = Measure("Grid1_NoPayload", "无载荷腿（停放重取稳态）", () =>
            {
                UIService.ShowUI<AllocPlainWindow>("AllocNoPayload");
                UIService.CloseUI<AllocPlainWindow>("AllocNoPayload");
            });

            Assert.AreEqual(0, events, "无载荷往返稳态不得有任何托管分配事件");
            AssertIdentityAfterRound<AllocPlainWindow>("AllocNoPayload");
        }

        /// <summary>格 2：静态腿 struct DTO（停放重取稳态）——承诺 0 事件（泛型直塞不装箱）。</summary>
        [Test]
        public void StaticLegStructDto_SteadyStateAllocatesZero()
        {
            var dto = new AllocDto { Value = 7, Text = "seven" };
            var events = Measure("Grid2_StaticLegStruct", "静态腿 struct DTO", () =>
            {
                UIService.ShowUI<AllocStructWindow, AllocDto>("AllocStruct", in dto);
                UIService.CloseUI<AllocStructWindow>("AllocStruct");
            });

            Assert.AreEqual(0, events, "静态腿 struct 载荷经泛型直塞通道不得装箱");
            var window = AssertIdentityAfterRound<AllocStructWindow>("AllocStruct");
            Assert.AreEqual(7, window.Payload.Value, "量具前提坏了：载荷落进了槽");
        }

        /// <summary>格 3：静态腿 class DTO——承诺 0 事件（存的是引用本身）。</summary>
        [Test]
        public void StaticLegClassDto_SteadyStateAllocatesZero()
        {
            var box = new AllocBox();
            var events = Measure("Grid3_StaticLegClass", "静态腿 class DTO", () =>
            {
                UIService.ShowUI<AllocClassWindow, AllocBox>("AllocStaticClass", in box);
                UIService.CloseUI<AllocClassWindow>("AllocStaticClass");
            });

            Assert.AreEqual(0, events, "静态腿 class 载荷只写引用，不复制不装箱");
            var window = AssertIdentityAfterRound<AllocClassWindow>("AllocStaticClass");
            Assert.AreSame(box, window.Payload, "落进槽的是调用方那个引用");
        }

        /// <summary>格 4：动态腿 class（<see cref="UIPayload"/> 擦除）——承诺 0 事件（引用型只存引用）。</summary>
        [Test]
        public void DynamicLegClass_SteadyStateAllocatesZero()
        {
            var box = new AllocBox();
            var events = Measure("Grid4_DynamicLegClass", "动态腿 class（UIPayload 擦除）", () =>
            {
                UIService.ShowUI(typeof(AllocClassWindow), "AllocDynClass", UIPayload.From(box));
                UIService.CloseUI<AllocClassWindow>("AllocDynClass");
            });

            Assert.AreEqual(0, events, "动态腿的引用型载荷按契约零分配：擦除载体只存引用");
            var window = AssertIdentityAfterRound<AllocClassWindow>("AllocDynClass");
            Assert.AreSame(box, window.Payload, "擦除后按 TArg 取回的是同一个引用");
        }

        /// <summary>格 5：动态腿基元（int）——每次开窗允许装箱一次，按常数事件预算守。</summary>
        [Test]
        public void DynamicLegPrimitive_WithinBoxingEventBudget()
        {
            var events = Measure("Grid5_DynamicLegPrimitive", "动态腿基元 int（允许装箱一次）", () =>
            {
                UIService.ShowUI(typeof(AllocIntWindow), "AllocDynInt", UIPayload.From(7));
                UIService.CloseUI<AllocIntWindow>("AllocDynInt");
            });

            Assert.LessOrEqual(events, PRIMITIVE_BOXING_EVENTS_PER_ITERATION * ITERATIONS,
                "动态腿基元的装箱预算是每次开窗一个 int：超出即契约坏了（不抬阈值，先查量具）");
            var window = AssertIdentityAfterRound<AllocIntWindow>("AllocDynInt");
            Assert.AreEqual(7, window.Payload, "量具前提坏了：装箱后按 TArg 取回原值");
        }

        /// <summary>格 6：<c>default(ct)</c> 全链（对照格）——不收令牌的腿不建取消源、不登记，承诺 0 事件。</summary>
        [Test]
        public void DefaultCancellationToken_FullChainAllocatesZero()
        {
            var events = Measure("Grid6_DefaultToken", "default(ct) 全链（对照格）", () =>
            {
                UIService.ShowUIAsync<AllocPlainWindow>("AllocNoCt", default);
                UIService.CloseUI<AllocPlainWindow>("AllocNoCt");
            });

            Assert.AreEqual(0, events, "default(ct) 那一档不得产生任何取消侧分配");
            AssertIdentityAfterRound<AllocPlainWindow>("AllocNoCt");
        }

        #endregion

        #region 诊断（临时）[DIAGNOSTIC - TEMPORARY]

        /// <summary>
        /// 分半定域（临时格，取完数即删）：把「开+关」往返那一次分配钉到开窗侧还是关窗侧。
        /// </summary>
        /// <remarks>
        /// 先把 <see cref="DIAG_IDS"/> 枚标识各跑一次「开+关」预热成停放态，再分两个测量窗各做单侧动作：
        /// 第一窗只做「停放→打开」（每发换一枚，保证走的是同一条复用支路、且窗不在栈上），
        /// 第二窗只做「打开→停放」。哪一侧读数非零，那一次分配就在那一侧。
        /// </remarks>
        [Test]
        public void Diag_SplitOpenAndCloseHalf()
        {
            const int ids = 12;
            var opened = new List<string>(ids);
            for (var i = 0; i < ids; i++)
            {
                var windowId = "DiagSplit" + i.ToString(CultureInfo.InvariantCulture);
                UIService.ShowUI<AllocPlainWindow>(windowId);
                var shell = UIService.GetWindow<AllocPlainWindow>(windowId);
                Assert.IsNotNull(shell, "诊断量具前提坏了：{0} 开不出来", windowId);
                Track(shell);
                opened.Add(windowId);
                UIService.CloseUI<AllocPlainWindow>(windowId);
            }

            var cursor = 0;
            var openEvents = AllocationCapture.MeasureManaged("DiagOpenHalf", DIAG_ITERATIONS,
                () =>
                {
                    var windowId = opened[cursor++];
                    UIService.ShowUI<AllocPlainWindow>(windowId);
                }, null);

            var closeCursor = 0;
            var closeEvents = AllocationCapture.MeasureManaged("DiagCloseHalf", DIAG_ITERATIONS,
                () => UIService.CloseUI<AllocPlainWindow>(opened[closeCursor++]), null);

            s_Lines.Add("DiagOpenHalf | 单侧：停放→打开 | allocEvents=" + openEvents +
                        " | iterations=" + DIAG_ITERATIONS);
            s_Lines.Add("DiagCloseHalf | 单侧：打开→停放 | allocEvents=" + closeEvents +
                        " | iterations=" + DIAG_ITERATIONS);
        }

        #endregion

        #region 量具 [INSTRUMENT]

        /// <summary>
        /// 量一格稳态往返的分配事件数。
        /// </summary>
        /// <remarks>
        /// 预热与建窗都由 <see cref="AllocationCapture"/> 安排在测量窗外；这里只把读数记进导出表。 <br />
        /// 可判假性已在提交里留证：本文件曾把 <c>round()</c> 与一次必然分配（<c>new byte[4096]</c>）同窗计量，
        /// 六格全体判红（<c>run red1 | passed 1 | failed 6</c>）——量具与夹具这条链路是通的，别改回字节前后差。
        /// </remarks>
        private static int Measure(string grid, string label, Action round)
        {
            var events = AllocationCapture.MeasureManaged(grid, ITERATIONS, round, null);

            s_Lines.Add(grid + " | " + label + " | allocEvents=" +
                        events.ToString(CultureInfo.InvariantCulture) +
                        " | eventsPerIteration=" + (events / (double)ITERATIONS).ToString("0.###", CultureInfo.InvariantCulture) +
                        " | iterations=" + ITERATIONS);
            return events;
        }

        /// <summary>结构档：往返之后栈上必须仍是同一个实例（量具失效时的真判据，不抬阈值）。</summary>
        private T AssertIdentityAfterRound<T>(string windowId) where T : UGUIWindow, new()
        {
            UIService.ShowUI<T>(windowId);
            var first = UIService.GetWindow<T>(windowId);
            Assert.IsNotNull(first, "量具前提坏了：{0} 这一标识要开得起来", windowId);
            Track(first);
            UIService.CloseUI<T>(windowId);
            UIService.ShowUI<T>(windowId);
            var second = UIService.GetWindow<T>(windowId);
            Assert.AreSame(first, second, "停放重取交回的必须是同一个实例");
            return second;
        }

        /// <summary>把窗口此刻的面板本体登记进出门清理表。</summary>
        private void Track(UIWindow window)
        {
            _trackedShells.Add(window?.gameObject);
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>代码建出的 uGUI 面板：只带一个 <see cref="Canvas"/>——<c>BindPanel</c> 认的就是这个组件。</summary>
        private static GameObject NewCodeUGUIPanel(string name)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.SetActive(true);
            panel.transform.SetParent(UIService.UIRoot, false);
            panel.AddComponent<Canvas>();
            return panel;
        }

        /// <summary>基准用的强类型 struct DTO：静态腿按值直达的那个。</summary>
        internal struct AllocDto
        {
            internal int Value;
            internal string Text;
        }

        /// <summary>基准用的 class DTO：两腿都只存引用的那个。</summary>
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

        /// <summary>class DTO 探针窗（缓存实例）：静态腿与动态腿共用这个，两格各用各的窗口标识。</summary>
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
