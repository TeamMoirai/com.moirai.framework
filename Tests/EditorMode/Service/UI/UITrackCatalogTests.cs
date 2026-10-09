using System;
using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// 轨道目录的登记·分派·收口用例：合成第三轨证明「加一轨不碰主文件」的每一档可观测行为。
    /// </summary>
    /// <remarks>
    /// 合成轨不造 <see cref="UIServiceHandler"/> 子类（<c>[Serializable]</c> 基类禁令——<c>SerializeReference</c> 类型扫描会把它塞进生产资产的 Inspector 下拉框）， <br />
    /// 只登记一枚 <see cref="UITrack"/>：认窗判据、有效性探针与 Type 形开窗实现全由用例自造，探针窗直承中性的 <see cref="UIWindow"/>。 <br />
    /// 进门先归位（两枚内建槽清空）、出门把合成轨从目录里摘干净——目录是跨用例的静态位，漏摘就是给后跑的用例留轨。 <br />
    /// 线程契约：仅主线程（EditMode 用例即主线程）。
    /// </remarks>
    [TestFixture]
    public sealed class UITrackCatalogTests
    {
        private readonly List<UITrack> _syntheticTracks = new List<UITrack>();

        /// <summary>进门归位：内建槽清回「干净域」，目录里只剩两枚内建自登记的轨。</summary>
        [SetUp]
        public void SetUp()
        {
            UIService.Internal_ResetHandlerSlots();
        }

        /// <summary>出门收尾：合成轨逐一摘登记、认领态整批清，不给后跑的用例留轨。</summary>
        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _syntheticTracks.Count; i++)
            {
                UIService.Internal_UnregisterTrack(_syntheticTracks[i]);
            }

            _syntheticTracks.Clear();
            UIService.Internal_ResetHandlerSlots();
        }

        #region 内建登记 [BUILT-IN REGISTRATION]

        /// <summary>
        /// 两枚内建轨各以自述登记进目录：轨道名、窗口基类与关停档位都在各自的 partial 里自报，目录序按档位升序。
        /// </summary>
        /// <remarks>
        /// 「主文件零改动」的前提是内建轨自己登记自己：摘掉任一轨 partial 里的登记语句，这一格当场红在对应那半段。 <br />
        /// 目录序按 <c>ShutdownOrder</c> 升序（同档按登记序）：UI Toolkit 取默认档排在 uGUI 那枚宿主档之前——关停次序由它表述。
        /// </remarks>
        [Test]
        public void BuiltinTracks_BothPartialFiles_RegisterThemselvesWithOwnShutdownOrders()
        {
            var tracks = UIService.Internal_PeekTracks();
            Assert.IsNotNull(tracks, "量具前提坏了：目录没建出来");
            Assert.GreaterOrEqual(tracks.Count, 2, "量具前提坏了：两枚内建轨至少各占一席");

            var uguiIndex = -1;
            var uikitIndex = -1;
            for (var i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].TrackName == "UGUI")
                {
                    uguiIndex = i;
                    Assert.AreEqual(typeof(UGUIWindow), tracks[i].WindowBaseType, "uGUI 轨自述的窗口基类就是 UGUIWindow");
                    Assert.AreEqual(UITrack.SHUTDOWN_ORDER_HOST, tracks[i].ShutdownOrder, "uGUI 轨自报宿主档：它那枚根是别轨壳的父级，最后收");
                }
                else if (tracks[i].TrackName == "UITK")
                {
                    uikitIndex = i;
                    Assert.AreEqual(typeof(UITKWindow), tracks[i].WindowBaseType, "UI Toolkit 轨自述的窗口基类就是 UITKWindow");
                    Assert.AreEqual(UITrack.SHUTDOWN_ORDER_DEFAULT, tracks[i].ShutdownOrder, "UI Toolkit 轨自报默认档：排在宿主轨之前");
                }
            }

            Assert.AreNotEqual(-1, uguiIndex, "目录里没有 uGUI 轨的登记：UIService.UGUI.cs 的静态自登记没跑");
            Assert.AreNotEqual(-1, uikitIndex, "目录里没有 UI Toolkit 轨的登记：UIService.UITK.cs 的静态自登记没跑");
            Assert.Less(uikitIndex, uguiIndex, "目录序按关停档位升序：默认档的 UI Toolkit 排在宿主档的 uGUI 之前");
        }

        #endregion

        #region 第三轨分派 [THIRD-TRACK DISPATCH]

        /// <summary>
        /// 合成第三轨登记后，<c>Type</c> 形入口把窗口分派给它自述的开窗实现：主文件一行都不为它改。
        /// </summary>
        /// <remarks>
        /// 判据是「交进合成轨那枚委托的实参逐枚对得上」，不是「栈上多了窗」——合成轨不往栈里写，门面分派到此为止； <br />
        /// 同步与异步两枚入口各走一遍：<c>isAsync</c> 分别落真与落 <c>SYNC_LOAD_USES_ASYNC</c>（编辑器那一档为假）。 <br />
        /// 把分派改回「按内建两轨 if/else」，这一格红在「委托根本没被叫到」上——第三轨就是这么被挡在门外的。
        /// </remarks>
        [Test]
        public void ShowUI_TypeEntry_ThirdRegisteredTrack_DispatchesToItsOwnOpenImpl()
        {
            Type passedType = null;
            var passedAsync = false;
            string passedWindowId = null;
            var passedFromResources = false;
            UIPayload passedPayload = UIPayload.Empty;
            CancellationToken passedToken = default;
            var payload = new object();
            RegisterSyntheticTrack("SYNTH", typeof(ProbeNeutralWindow), UITrack.SHUTDOWN_ORDER_DEFAULT,
                (type, isAsync, windowId, fromResources, erased, ct) =>
                {
                    passedType = type;
                    passedAsync = isAsync;
                    passedWindowId = windowId;
                    passedFromResources = fromResources;
                    passedPayload = erased;
                    passedToken = ct;
                }, NeverValid);

            UIService.ShowUIAsync(typeof(ProbeNeutralWindow), "SynthAsync", true, UIPayload.From(payload));
            Assert.AreEqual(typeof(ProbeNeutralWindow), passedType, "交进合成轨的就是调用方给的窗口类");
            Assert.IsTrue(passedAsync, "异步入口落下异步档");
            Assert.AreEqual("SynthAsync", passedWindowId, "窗口标识原样交下（栈上身份与地址原料同这一枚）");
            Assert.IsTrue(passedFromResources, "取法原样交下");
            Assert.IsFalse(passedPayload.IsEmpty, "擦除后的载荷按一枚 UIPayload 收下");
            Assert.AreSame(payload, passedPayload.To<object>(), "交进轨道的就是调用方那一枚");
            Assert.IsFalse(passedToken.CanBeCanceled, "缺省令牌一路是 None 档：动态腿不收 CT 时不造可撤销源");
            Assert.IsNull(UIService.SharedLedger.GetTopWindow(), "合成轨不往栈里写：分派到此为止，栈上一只窗都不多");

            UIService.ShowUI(typeof(ProbeNeutralWindow), "SynthSync", false, UIPayload.From(payload));
            Assert.AreEqual("SynthSync", passedWindowId, "同步入口落到同一枚实现");
            Assert.IsFalse(passedAsync, "同步入口在编辑器那一档不落异步");
        }

        /// <summary>
        /// 合成轨的窗口基类也进「认不出轨」的枚举文案：登记进来的轨都答得出，摘登记后回落在抬错上。
        /// </summary>
        /// <remarks>
        /// 文案按目录枚举可挑的基类——把枚举改回写死的两枚内建名，合成轨那半段红； <br />
        /// 摘登记后再开合成窗回「认不出轨」那一档——目录真把这一轨收回去了，不是探针失灵。
        /// </remarks>
        [Test]
        public void ShowUI_UnclaimedWindowAfterSyntheticRegistration_EnumeratesItsBaseThenRejectsAfterUnregister()
        {
            var synth = RegisterSyntheticTrack("SYNTH-MSG", typeof(ProbeNeutralWindow), UITrack.SHUTDOWN_ORDER_DEFAULT,
                DelegateSink.None, NeverValid);

            var error = Assert.Throws<GameException>(() => UIService.ShowUI(typeof(ProbeUnclaimedWindow), "w"),
                "不落任何一轨的窗口类照旧当场抬错");
            StringAssert.Contains("ProbeNeutralWindow", error.Message, "文案按目录枚举：合成轨的窗口基类也要答得出");
            StringAssert.Contains("UGUIWindow", error.Message, "内建轨的窗口基类照旧在列");

            Assert.IsTrue(UIService.Internal_UnregisterTrack(synth), "摘登记要真把合成轨从目录里拿掉");
            _syntheticTracks.Remove(synth);
            Assert.Throws<GameException>(() => UIService.ShowUI(typeof(ProbeNeutralWindow), "w"),
                "摘掉登记后合成轨的窗口类回「认不出轨」那一档");
        }

        #endregion

        #region 有效性聚合 [VALIDITY AGGREGATION]

        /// <summary>
        /// 门面有效性按目录聚合：合成轨的探针为真时门面算有效，摘掉登记后回无效——内建两轨一枚都没认领。
        /// </summary>
        /// <remarks>
        /// 探针交回门面、目录不另记在位状态：把 <see cref="UIService.IsValid"/> 收成「只认两枚内建槽」的写法，这一格红在合成轨探针那半段。 <br />
        /// 起步先归位过：两枚内建槽此刻都是空的，门面的真假全由合成轨探针答。
        /// </remarks>
        [Test]
        public void IsValid_ThirdTrackProbe_AggregatesIntoFacadeValidity()
        {
            var claimed = false;
            RegisterSyntheticTrack("SYNTH-VALID", typeof(ProbeNeutralWindow), UITrack.SHUTDOWN_ORDER_DEFAULT,
                DelegateSink.None, () => claimed);

            Assert.IsFalse(UIService.IsValid, "内建两轨没认领、合成轨探针为假：门面无效");
            claimed = true;
            Assert.IsTrue(UIService.IsValid, "合成轨探针为真：目录里任何一轨在位门面就算有效");

            claimed = false;
            Assert.IsFalse(UIService.IsValid, "探针答的是轨自己的槽位，不是目录里记过一份");
        }

        #endregion

        #region 关停次序 [SHUTDOWN ORDER]

        /// <summary>
        /// 关停按各轨自报的档位升序收口：默认档在前、中间档随后、宿主档最后；没认领的轨一次都不被叫到。
        /// </summary>
        /// <remarks>
        /// 三枚合成轨各带一枚记账回调、经 <c>AttachDriver</c> 认领（认领的是回调，不需要驱动者实例—— <br />
        /// 合成轨因此不造 <see cref="UIServiceHandler"/> 子类）；第四枚只登记不认领，它的记账一次都不该出现。 <br />
        /// 把收口改成「按登记序」或「倒序」，这一格红在次序上；把「只叫认领过的」改成「目录里都叫」，第四枚那半段红。
        /// </remarks>
        [Test]
        public void OnShutdown_ThreeClaimedSyntheticTracks_CloseInAscendingShutdownOrder()
        {
            var order = new List<string>();
            var defaultTrack = RegisterSyntheticTrack("SYNTH-DEFAULT", typeof(ProbeNeutralWindow),
                UITrack.SHUTDOWN_ORDER_DEFAULT, DelegateSink.None, NeverValid);
            var middleTrack = RegisterSyntheticTrack("SYNTH-MIDDLE", typeof(ProbeNeutralWindow), 50,
                DelegateSink.None, NeverValid);
            var hostTrack = RegisterSyntheticTrack("SYNTH-HOST", typeof(ProbeNeutralWindow),
                UITrack.SHUTDOWN_ORDER_HOST, DelegateSink.None, NeverValid);
            RegisterSyntheticTrack("SYNTH-UNCLAIMED", typeof(ProbeNeutralWindow), 75,
                DelegateSink.None, NeverValid);

            defaultTrack.AttachDriver(() => order.Add("default"));
            middleTrack.AttachDriver(() => order.Add("middle"));
            hostTrack.AttachDriver(() => order.Add("host"));

            new UIService().OnShutdown();

            CollectionAssert.AreEqual(new[] { "default", "middle", "host" }, order,
                "按自报档位升序收口：默认档先、中间档随后、宿主档最后，没认领的那枚一次都没被叫到");
            Assert.IsNull(defaultTrack.ClaimedShutDown, "收口之后认领态被整批清掉，下一轮认领重新挂");
            Assert.IsNull(middleTrack.ClaimedShutDown, "同上");
            Assert.IsNull(hostTrack.ClaimedShutDown, "同上");
        }

        #endregion

        #region 夹具 [FIXTURE]

        /// <summary>造一枚合成轨并登记进目录：认窗判据按给定窗口基类（含派生），出门由 <see cref="TearDown"/> 摘登记。</summary>
        /// <param name="name">轨道名。</param>
        /// <param name="windowBaseType">本轨认的窗口基类。</param>
        /// <param name="shutdownOrder">关停档位。</param>
        /// <param name="openSink">Type 形入口的分派落点，由用例记账。</param>
        /// <param name="validProbe">有效性探针，由用例控制。</param>
        /// <returns>登记进目录的那一枚。</returns>
        private UITrack RegisterSyntheticTrack(string name, Type windowBaseType, int shutdownOrder,
            Action<Type, bool, string, bool, UIPayload, CancellationToken> openSink, Func<bool> validProbe)
        {
            var track = new UITrack(name, windowBaseType, shutdownOrder,
                windowBaseType.IsAssignableFrom, validProbe, openSink);
            UIService.Internal_RegisterTrack(track);
            _syntheticTracks.Add(track);
            return track;
        }

        /// <summary>恒假的有效性探针：合成轨大多数用例只需要「不在位」。</summary>
        private static bool NeverValid() => false;

        /// <summary>空落点：不需要记账分派的用例交这一枚。</summary>
        private static class DelegateSink
        {
            /// <summary>什么都不做的开窗落点。</summary>
            internal static void None(Type type, bool isAsync, string windowId, bool fromResources,
                UIPayload payload, CancellationToken ct)
            {
            }
        }

        /// <summary>合成轨认的窗口基类：直承中性的 <see cref="UIWindow"/>，不挂任何内建轨的基类。</summary>
        private sealed class ProbeNeutralWindow : UIWindow
        {
        }

        /// <summary>无主样本：直承 <see cref="UIWindow"/>，登记再多合成轨也没人认它。</summary>
        private sealed class ProbeUnclaimedWindow : UIWindow
        {
        }

        #endregion
    }
}
