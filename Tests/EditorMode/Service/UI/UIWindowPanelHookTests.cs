using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos.Tests.EditorMode;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// <see cref="UIWindow"/> 的面板钩子契约（<c>LoadPanel</c> / <c>LoadPanelAsync</c> / <c>ApplyVisible</c> / <c>ApplyDepth</c> / <c>ApplyInteractable</c> / <c>ParkPanel</c> / <c>DestroyPanel</c>）与意图位语义。
    /// </summary>
    /// <remarks>
    /// 钉三件事：① 绑面板时的钩子次序 <c>LoadPanel</c>→<c>ApplyVisible</c>→<c>ApplyDepth</c>→<c>ApplyInteractable</c>（后端在 <c>LoadPanel</c> 里装配面板，基类随即把三份意图落到刚出现的面板上，最后才通知准备回调）； <br />
    /// ② <c>Visible</c> / <c>Depth</c> / <c>Interactable</c> 以<b>意图</b>为准——同值二次赋值不再重复调钩子。旧实现是拿 <c>canvas.sortingOrder</c> 与实际 Unity layer 相比， <br />
    /// 那是后端内部事实而非契约（改判 R1 ①），差异另在 <c>UGUIWindowTests</c> 里按现状登记； <br />
    /// ③ 排序刷新（<c>_OnSortDepth</c> / <c>_isSortingOrderDirty</c>）仍由基类决策，脏位在 <c>Visible</c> 转真时结算。 <br />
    /// 探针窗口覆写钩子并记录调用序，夹具不建 Canvas、不碰 <c>ResourceService</c>：本文件只测「基类在何时叫了什么钩子」，面板落地细节归 <c>UGUIWindowTests</c>。
    /// </remarks>
    [TestFixture]
    public sealed class UIWindowPanelHookTests
    {
        #region 绑面板的钩子次序 [LOAD ORDER]

        /// <summary>同步装载：钩子次序必须是 LoadPanel→ApplyVisible→ApplyDepth→ApplyInteractable，准备回调排在最后。</summary>
        [Test]
        public void Load_SyncPanel_DrivesPanelHooksInContractOrder()
        {
            var window = new HookProbeWindow();
            window.Init(nameof(HookProbeWindow), 1, false, "Panel", false, 10, false);

            window.InternalLoad("Panel", _ => window.Calls.Add("PrepareCallback"), false, null);

            CollectionAssert.AreEqual(new[]
            {
                "LoadPanel:Panel:False",
                "ApplyVisible:False",
                "ApplyDepth:0",
                "ApplyInteractable:False",
                "PrepareCallback",
            }, window.Calls, "装载后必须按契约次序把三份意图落到新面板上，再通知准备回调");
            Assert.IsTrue(window.IsLoadDone, "面板装载成功必须置 IsLoadDone");
            Assert.IsTrue(window.IsPrepare, "面板装载成功必须置 IsPrepare");
        }

        /// <summary>装载意图取当前意图位：绑定前被写过的显隐/深度，绑定当场就要落到面板上，不得丢掉。</summary>
        [Test]
        public void Load_AfterIntentWrites_AppliesCurrentIntentNotDefaults()
        {
            var window = new HookProbeWindow();
            window.Init(nameof(HookProbeWindow), 1, false, "Panel", false, 10, false);
            window.Visible = true;
            window.Depth = 2200;
            window.Interactable = false;
            window.Calls.Clear();

            window.InternalLoad("Panel", null, false, null);

            CollectionAssert.AreEqual(new[]
            {
                "LoadPanel:Panel:False",
                "ApplyVisible:True",
                "ApplyDepth:2200",
                "ApplyInteractable:False",
            }, window.Calls, "绑定前积下的意图不得在装载时被默认值顶掉");
        }

        /// <summary>基类默认钩子是「加载不了面板」：装载停在未就绪态并报失败收口，不得抛、不得置位、不得回调。</summary>
        [Test]
        public void Load_WithoutBackendOverride_StaysUnloaded()
        {
            // 直挂 UIWindow：UGUIWindow 的 LoadPanel 会真去要资源，这里要钉的是基类默认实现
            var window = new BareWindow();
            window.Init(nameof(BareWindow), 1, false, "Panel", false, 10, false);
            var called = false;

            // 装载失败收口的那一条 Error（失败必须报出来，不许静默）
            UtfLogExpect.Error();
            window.InternalLoad("Panel", _ => called = true, false, null);

            Assert.IsFalse(window.IsLoadDone, "默认钩子加载失败时不得置 IsLoadDone");
            Assert.IsFalse(window.IsPrepare, "默认钩子加载失败时不得置 IsPrepare");
            Assert.IsFalse(called, "默认钩子加载失败时不得通知准备回调");
        }

        /// <summary>异步钩子的默认实现同样回 false，且必须回一个已完成的任务，调用方不得被挂在续体上。</summary>
        [Test]
        public void LoadPanelAsync_DefaultHook_ReportsFailureSynchronously()
        {
            var window = new BareWindow();

            var awaiter = window.LoadPanelAsync("Panel", false, CancellationToken.None).GetAwaiter();

            Assert.IsTrue(awaiter.IsCompleted, "默认异步钩子必须回已完成的任务（EditMode 无 PlayerLoop，挂续体等于永不返回）");
            Assert.IsFalse(awaiter.GetResult(), "默认异步钩子不得谎报加载成功");
        }

        #endregion

        #region 意图位与钩子的重复抑制 [INTENT]

        /// <summary><c>Visible</c> 同值二次赋值只落一次钩子、只发一次 <c>OnSetVisible</c>：判据是意图位，不是面板实际 layer。</summary>
        [Test]
        public void Visible_SameValueTwice_AppliesHookOnce()
        {
            var window = Created();

            window.Visible = true;
            window.Visible = true;

            Assert.AreEqual(1, window.CountOf("ApplyVisible:True"), "同值二次赋值不得重复落钩子");
            Assert.AreEqual(1, window.SetVisibleCount, "OnSetVisible 也只随意图变化发一次");
        }

        /// <summary><c>Depth</c> 同值二次赋值只落一次钩子，getter 回意图值。</summary>
        [Test]
        public void Depth_SameValueTwice_AppliesHookOnce()
        {
            var window = Created();

            window.Depth = 900;
            window.Depth = 900;

            Assert.AreEqual(1, window.CountOf("ApplyDepth:900"), "同值二次赋值不得重复落钩子");
            Assert.AreEqual(900, window.Depth, "Depth 的 getter 回意图值");
        }

        /// <summary><c>Interactable</c> 同值二次赋值只落一次钩子；意图未变时一个都不落。</summary>
        [Test]
        public void Interactable_SameValueTwice_AppliesHookOnce()
        {
            var window = Created();
            var start = window.Interactable;

            window.Interactable = start;
            Assert.AreEqual(0, window.CountOf($"ApplyInteractable:{start}"), "同值不得落钩子");

            window.Interactable = !start;
            window.Interactable = !start;
            Assert.AreEqual(1, window.CountOf($"ApplyInteractable:{!start}"), "只有意图变化才落钩子");
        }

        /// <summary>意图位口径（改判 R1 ①）：<c>Visible</c> 的 getter 读调用方要的值，不再回读面板 layer。</summary>
        /// <remarks>
        /// 旧实现 getter 读 <c>canvas.gameObject.layer == WINDOW_SHOW_LAYER</c>，面板被外部改层时读数跟着变；本格钉的是新口径，不是为旧口径背书。 <br />
        /// 探针的 <c>BackendVisibleState</c> 代表后端那一侧的事实（对应面板实际 layer），把它拧离意图即模拟面板被外部挪层。
        /// </remarks>
        [Test]
        public void Visible_GetterReportsIntent_NotPanelState()
        {
            var window = Created();

            window.Visible = true;
            Assert.IsTrue(window.BackendVisibleState, "前提：落钩子把后端事实写成与意图一致");

            window.BackendVisibleState = false;

            Assert.IsTrue(window.Visible, "getter 回意图，不回读后端事实（旧口径此处回 false）");
            Assert.AreEqual(1, window.CountOf("ApplyVisible:True"), "读 getter 不补落钩子，也不因后端事实偏离而重写");
        }

        #endregion

        #region 排序刷新仍归基类 [SORT REFRESH]

        /// <summary>可见时改 <c>Depth</c>：基类当场刷 <c>_OnSortDepth()</c>，不留脏位。</summary>
        [Test]
        public void Depth_ChangeWhileVisible_RefreshesSortDepth()
        {
            var window = Loaded();
            window.Visible = true;
            window.Calls.Clear();

            window.Depth = 900;

            Assert.AreEqual(new[] { "ApplyDepth:900", "OnSortDepth" }, window.Calls.ToArray(),
                "深度钩子只落一次，且排在基类的排序刷新之前（次序按 Calls 记录）");
            Assert.AreEqual(1, window.SortDepthCount, "可见状态下改深度必须刷一次子件排序");
            Assert.IsFalse(window.IsSortingOrderDirty, "已当场结算排序，不得留下脏位");
        }

        /// <summary>隐藏时改 <c>Depth</c> 只置脏位不刷；转可见时清脏并补刷一次。</summary>
        [Test]
        public void Depth_ChangeWhileHidden_MarksDirty_VisibleSettlesIt()
        {
            var window = Created();

            window.Depth = 900;

            Assert.AreEqual(0, window.SortDepthCount, "隐藏状态下改深度不得刷排序");
            Assert.IsTrue(window.IsSortingOrderDirty, "隐藏状态下改深度要留脏位");

            window.Calls.Clear();
            window.Visible = true;

            CollectionAssert.AreEqual(new[] { "ApplyVisible:True", "OnSortDepth", "OnSetVisible:True" }, window.Calls,
                "转可见只落一次显隐钩子，其后先结算攒下的排序、再按 _isCreate 发一次 OnSetVisible");
            Assert.AreEqual(1, window.SortDepthCount, "转可见时必须把攒下的深度补刷一次");
            Assert.IsFalse(window.IsSortingOrderDirty, "补刷后脏位必须清掉");
        }

        #endregion

        #region 停放与销毁的路由 [PARK & DESTROY]

        /// <summary>缓存实例销毁走 <c>ParkPanel</c>，不落 <c>DestroyPanel</c>：面板要留着下次开窗复用。</summary>
        [Test]
        public void Destroy_CachedInstance_RoutesToParkPanel()
        {
            var window = Created(true);

            window.InternalDestroy();

            CollectionAssert.Contains(window.Calls, "ParkPanel", "缓存实例的销毁只停放面板");
            CollectionAssert.DoesNotContain(window.Calls, "DestroyPanel", "缓存实例不得销毁面板物体");
        }

        /// <summary>非缓存实例销毁走 <c>DestroyPanel</c>，不落 <c>ParkPanel</c>。</summary>
        [Test]
        public void Destroy_NonCached_RoutesToDestroyPanel()
        {
            var window = Created(false);

            window.InternalDestroy();

            CollectionAssert.Contains(window.Calls, "DestroyPanel", "非缓存实例的销毁必须走面板销毁钩子");
            CollectionAssert.DoesNotContain(window.Calls, "ParkPanel", "销毁路径不经过停放");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>装载完钩子次序、停在「未创建」态的探针窗口：意图位为默认值，记录已清空到起点。</summary>
        private static HookProbeWindow Loaded()
        {
            var window = new HookProbeWindow();
            window.Init(nameof(HookProbeWindow), 1, false, "Panel", false, 10, false);
            window.InternalLoad("Panel", null, false, null);
            window.Calls.Clear();
            return window;
        }

        /// <summary>
        /// 同上再走一遍创建（<c>InternalCreate</c>），使 <c>_isCreate</c> 为真——<c>OnSetVisible</c> 与「转可见结算脏位」都以它为门槛。
        /// </summary>
        /// <remarks>准备回调里直调 <c>InternalCreate</c>，与生产侧 <c>UIServiceHandler.OnWindowPrepare</c> 同形；不开动画、不入栈，故不碰 <c>UIService</c> 的处理器。</remarks>
        private static HookProbeWindow Created(bool cacheInstance = false)
        {
            var window = new HookProbeWindow();
            window.Init(nameof(HookProbeWindow), 1, false, "Panel", false, 10, cacheInstance);
            window.InternalLoad("Panel", w => w.InternalCreate(), false, null);
            window.Calls.Clear();
            return window;
        }

        /// <summary>覆写全部面板钩子并记录调用序的探针窗口：只记账，不落地任何面板物体；<c>BackendVisibleState</c> 是它这边的「后端事实」。</summary>
        private sealed class HookProbeWindow : UGUIWindow
        {
            internal readonly List<string> Calls = new List<string>();
            internal int SortDepthCount;
            internal int SetVisibleCount;

            /// <summary>探针这边的后端事实，对应面板实际所在 layer；用例可把它拧离意图来模拟外部改层。</summary>
            internal bool BackendVisibleState;

            protected internal override bool LoadPanel(string assetLocation, bool fromResources)
            {
                Calls.Add($"LoadPanel:{assetLocation}:{fromResources}");
                return true;
            }

            protected internal override void ApplyVisible(bool value)
            {
                Calls.Add($"ApplyVisible:{value}");
                BackendVisibleState = value;
            }

            protected internal override void ApplyDepth(int value) => Calls.Add($"ApplyDepth:{value}");

            protected internal override void ApplyInteractable(bool value) => Calls.Add($"ApplyInteractable:{value}");

            protected internal override void ParkPanel() => Calls.Add("ParkPanel");

            protected internal override void DestroyPanel() => Calls.Add("DestroyPanel");

            protected override void OnSortDepth()
            {
                SortDepthCount++;
                Calls.Add("OnSortDepth");
            }

            protected override void OnSetVisible(bool visible)
            {
                SetVisibleCount++;
                Calls.Add($"OnSetVisible:{visible}");
            }

            internal int CountOf(string call)
            {
                var count = 0;
                for (var i = 0; i < Calls.Count; i++)
                {
                    if (Calls[i] == call)
                    {
                        count++;
                    }
                }

                return count;
            }

            internal bool IsSortingOrderDirty => _isSortingOrderDirty;
        }

        /// <summary>不覆写任何钩子的 <see cref="UIWindow"/>：用于钉基类默认实现（加载不了面板、钩子全空）。</summary>
        private sealed class BareWindow : UIWindow
        {
        }

        #endregion
    }
}
