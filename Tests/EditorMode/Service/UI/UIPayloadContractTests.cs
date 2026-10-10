using System;
using Moirai.Atropos;
using Moirai.Atropos.UI;
using NUnit.Framework;
using UnityEngine;

namespace Service.UI
{
    /// <summary>载荷格用的强类型 DTO：struct 一个，验静态腿按值直达且不经擦除。</summary>
    internal struct ProbeDto
    {
        internal int Value;
        internal string Text;
    }

    /// <summary>带 struct 载荷槽的探针窗（缓存实例）：擦除落点计数，面板自带真实物体供停放/重取摸得到。</summary>
    [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
    internal sealed class SlotProbeWindow : UGUIWindow<ProbeDto>
    {
        /// <summary>擦除通道（<c>Internal_SetPayload(UIPayload)</c>）被叫到的次数：静态腿必须一次都不叫。</summary>
        internal int ErasedSetCount;

        private GameObject _panel;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(SlotProbeWindow));
            return true;
        }

        protected internal override void ParkPanel() { }

        internal override void Internal_SetPayload(UIPayload payload)
        {
            ErasedSetCount++;
            base.Internal_SetPayload(payload);
        }
    }

    /// <summary>带 class 载荷槽的探针窗（缓存实例）：与 struct 那个同形，判引用同一性。</summary>
    [Window(EUILayer.Tips, cacheTimeToDestroy: -1f)]
    internal sealed class SlotRefProbeWindow : UGUIWindow<object>
    {
        /// <summary>擦除通道被叫到的次数。</summary>
        internal int ErasedSetCount;

        private GameObject _panel;

        public override GameObject gameObject => _panel;

        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            _panel = new GameObject(nameof(SlotRefProbeWindow));
            return true;
        }

        protected internal override void ParkPanel() { }

        internal override void Internal_SetPayload(UIPayload payload)
        {
            ErasedSetCount++;
            base.Internal_SetPayload(payload);
        }
    }

    /// <summary>不带载荷槽的探针窗：被塞非空载荷必须在账本那一道当场抬错。</summary>
    [Window(EUILayer.Tips)]
    internal sealed class SlotlessProbeWindow : UGUIWindow
    {
        protected internal override bool LoadPanel(string assetLocation, bool fromResources) => true;
    }

    /// <summary>
    /// 载荷槽契约（spec §9.1 契约格全集）：静态腿泛型直塞、动态腿擦除、无槽与非符槽型一律 fail-fast、载荷残留按覆盖语义。
    /// </summary>
    /// <remarks>
    /// 两条通道各自的可观测判据：<see cref="IUIPayloadSlot{TArg}"/> 那一条按 <typeparamref name="TArg"/> 直塞（struct 不装箱、擦除计数为 0），
    /// <see cref="UIPayload"/> 那一条经 <c>Internal_SetPayload</c> 落位（擦除计数 +1）——同一个窗两个计数分开数，混用当场红。 <br />
    /// 判假的两档都在压栈与回执之前：<see cref="UIWindow.Internal_SetPayload"/>（无槽却给非空擦除载荷）与
    /// <c>UIWindowLedger.SetPayloadChecked</c>（按标识命中的窗槽型不符）一律 <see cref="GameException"/>，不开半个窗、不静默退化。 <br />
    /// 直账本驱动（<c>new UIWindowLedger()</c>）：本文件的格子不碰关停回滚，也不需要驱动者在位。面板本体按 DestroyImmediate 收，不留给下一轮；
    /// 登记进清理表排在<b>取到窗口的那一刻</b>，断言判红也不给下一轮用例留一个活物体。 <br />
    /// 停放支路的槽型校验按<b>意图次序</b>（先验槽、后卸停放）多钉一格：<c>GenericChannel_WrongSlotTypeOnParkedWindow_ParkedStateNotConsumedByThrow</c>
    /// 在 R2 的 Runtime 修复落地前判红，转绿即证悬空态已收（见那一格的 remarks）。 <br />
    /// 线程契约：仅主线程。
    /// </remarks>
    [TestFixture]
    public sealed class UIPayloadContractTests
    {
        private UIWindowLedger _ledger;
        private readonly System.Collections.Generic.List<GameObject> _panels =
            new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _ledger = new UIWindowLedger();
        }

        [TearDown]
        public void TearDown()
        {
            // 探针窗自带的真实面板按 DestroyImmediate 收：编辑模式里 Object.Destroy 当场报错
            for (var i = 0; i < _panels.Count; i++)
            {
                if (_panels[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_panels[i]);
                }
            }

            _panels.Clear();
            _ledger = null;
        }

        [Test]
        public void StaticLeg_StructDto_LandsByValueWithoutErasure()
        {
            var dto = new ProbeDto { Value = 7, Text = "seven" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "SlotStruct", null, in dto);

            var window = (SlotProbeWindow)_ledger.GetWindow("SlotStruct");
            Assert.IsNotNull(window, "量具前提坏了：静态腿要开出那个带槽窗");
            _panels.Add(window.gameObject);
            Assert.AreEqual(7, window.Payload.Value, "struct 载荷按值直达字段");
            Assert.AreEqual("seven", window.Payload.Text, "struct 载荷按值直达字段");
            Assert.AreEqual(0, window.ErasedSetCount, "静态腿一步都不经 UIPayload 擦除通道");
        }

        [Test]
        public void StaticLeg_ClassDto_LandsByReferenceWithoutErasure()
        {
            var payload = new object();
            _ledger.ShowUIImp<object>(typeof(SlotRefProbeWindow), false, "SlotRef", null, in payload);

            var window = (SlotRefProbeWindow)_ledger.GetWindow("SlotRef");
            _panels.Add(window.gameObject);
            Assert.AreSame(payload, window.Payload, "class 载荷存的是引用本身");
            Assert.AreEqual(0, window.ErasedSetCount, "class 载荷同样走泛型直塞，不落到擦除通道");
        }

        [Test]
        public void DynamicLeg_ErasedPayload_ReachesSlotThroughErasure()
        {
            var payload = new object();
            _ledger.ShowUIImp(typeof(SlotRefProbeWindow), false, "SlotDyn", null, UIPayload.From(payload));

            var window = (SlotRefProbeWindow)_ledger.GetWindow("SlotDyn");
            _panels.Add(window.gameObject);
            Assert.AreSame(payload, window.Payload, "动态腿擦除后按 TArg 取回同一个引用");
            Assert.AreEqual(1, window.ErasedSetCount, "动态腿唯一的落点是擦除通道");
        }

        [Test]
        public void DynamicLeg_StructDto_UnboxesIntoSlot()
        {
            var dto = new ProbeDto { Value = 3, Text = "three" };
            _ledger.ShowUIImp(typeof(SlotProbeWindow), false, "SlotDynStruct", null, UIPayload.From(dto));

            var window = (SlotProbeWindow)_ledger.GetWindow("SlotDynStruct");
            _panels.Add(window.gameObject);
            Assert.AreEqual(3, window.Payload.Value, "装箱一次后按 TArg 取回原值");
            Assert.AreEqual("three", window.Payload.Text, "装箱一次后按 TArg 取回原值");
            Assert.AreEqual(1, window.ErasedSetCount, "动态腿走擦除通道：这一档允许装箱一次");
        }

        [Test]
        public void SlotlessWindow_NonEmptyPayload_FailsFastBeforePush()
        {
            var ex = Assert.Throws<GameException>(() => _ledger.ShowUIImp(typeof(SlotlessProbeWindow), false, "Slotless",
                null, UIPayload.From("x")), "不带槽却给非空载荷必须当场抬错，不静默吞");
            StringAssert.Contains(nameof(SlotlessProbeWindow), ex.Message, "文案带窗口类，便于定位漏换基类的调用点");
            Assert.IsNull(_ledger.GetWindow("Slotless"), "抬错排在压栈之前：不开半个窗");

            // 空载荷作用于无槽窗是合法档：不带载荷的腿本就一路走这一档
            Assert.DoesNotThrow(() => _ledger.ShowUIImp(typeof(SlotlessProbeWindow), false, "SlotlessEmpty",
                null, UIPayload.Empty), "空载荷 + 无槽窗为合法组合");
            Assert.IsNotNull(_ledger.GetWindow("SlotlessEmpty"), "空载荷那一档照常开窗");
        }

        [Test]
        public void GenericChannel_WrongSlotTypeOnStackedWindow_FailsFast()
        {
            var dto = new ProbeDto { Value = 1, Text = "one" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "SlotMismatch", null, in dto);
            var window = (SlotProbeWindow)_ledger.GetWindow("SlotMismatch");
            _panels.Add(window.gameObject);

            var ex = Assert.Throws<GameException>(() => _ledger.ShowUIImp<string>(typeof(SlotProbeWindow), false,
                "SlotMismatch", null, "text"), "按标识命中的窗槽型不符时泛型直塞通道当场抬错，不退化为擦除");
            StringAssert.Contains(nameof(String), ex.Message, "文案带这一腿要塞的类型名");
        }

        /// <summary>停放支路同样校验槽型：抬错那一档不吃残留载荷。</summary>
        /// <remarks>
        /// 这一格判的两半在 R2 修复前后都成立（停放态有没有被消费掉不在这一格的射程里）：
        /// 「按标识命中停放窗也要验槽型」与「残留载荷未被吃」⇒ 另一半天数在 <see cref="GenericChannel_WrongSlotTypeOnParkedWindow_ParkedStateNotConsumedByThrow"/>。
        /// </remarks>
        [Test]
        public void GenericChannel_WrongSlotTypeOnParkedWindow_FailsFast()
        {
            var dto = new ProbeDto { Value = 1, Text = "one" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "SlotParked", null, in dto);
            var window = (SlotProbeWindow)_ledger.GetWindow("SlotParked");
            _panels.Add(window.gameObject);
            _ledger.CloseUI<SlotProbeWindow>("SlotParked");
            Assert.IsTrue(_ledger.IsParked("SlotParked"), "量具前提坏了：缓存实例的窗关闭后落在停放表里");

            Assert.Throws<GameException>(() => _ledger.ShowUIImp<string>(typeof(SlotProbeWindow), false,
                "SlotParked", null, "text"), "停放支路同样校验槽型：不按标识命中就万事大吉是假象");
            Assert.AreEqual(dto.Value, window.Payload.Value, "抬错那一档不吃载荷：残留位仍是上一次那一份");
        }

        /// <summary>停放支路的槽型校验排在<b>卸停放之前</b>：抬错那一档不消费停放态，那个实例仍从停放表取得回。</summary>
        /// <remarks>
        /// 判据按<b>意图次序</b>（先验槽型、后卸停放）写：<b>R2 修复即转绿</b>，修复前这一格判红——它守的就是 R2 那一处悬空态
        /// （现行 <c>ResolveOrStartLoad&lt;TArg&gt;</c> 的停放支路先 <c>CancelCacheTimer → SetActive(true) → _cache.Remove</c>，
        /// 再叫 <c>SetPayloadChecked</c> ⇒ 抬错时那个实例既离开停放表又没进栈，落在无人持有、再也取不到的悬空态）。 <br />
        /// 与次序无关的那一半先钉住：当场抬错、且栈上没有半开的那个（两句在两种次序里都成立）；
        /// 转绿时才亮起来的是 <c>IsParked</c> 那一句与「按对得上的槽型再开、取回的还是同一个实例」那两句。 <br />
        /// 那个窗的面板在本格开头就登记进清理表 <c>_panels</c>，判红也交 <see cref="TearDown"/> 收走，不留给下一轮。
        /// </remarks>
        [Test]
        public void GenericChannel_WrongSlotTypeOnParkedWindow_ParkedStateNotConsumedByThrow()
        {
            var dto = new ProbeDto { Value = 1, Text = "one" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "ParkedSurvive", null, in dto);
            var window = (SlotProbeWindow)_ledger.GetWindow("ParkedSurvive");
            _panels.Add(window.gameObject);
            _ledger.CloseUI<SlotProbeWindow>("ParkedSurvive");
            Assert.IsTrue(_ledger.IsParked("ParkedSurvive"), "量具前提坏了：关闭后落在停放表里");

            Assert.Throws<GameException>(() => _ledger.ShowUIImp<string>(typeof(SlotProbeWindow), false, "ParkedSurvive",
                null, "text"), "量具前提坏了：槽型不符仍按 R2 那一道抬错");

            Assert.IsTrue(_ledger.IsParked("ParkedSurvive"), "R2 修复转绿：抬错不消费停放态，那个实例仍留在停放表里");
            Assert.IsNull(_ledger.GetWindow("ParkedSurvive"), "抬错也不压栈：栈上没有半开的那个");

            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "ParkedSurvive", null, in dto);
            var reopened = _ledger.GetWindow<SlotProbeWindow>("ParkedSurvive");
            Assert.IsNotNull(reopened, "量具前提坏了：抬错之后再按对得上的槽型开，栈上要有一个");
            _panels.Add(reopened.gameObject);
            Assert.AreSame(window, reopened,
                "R2 修复转绿：取回的还是那个停放实例——悬空态已收，也没造第二个");
            Assert.AreEqual(1, _ledger.PeekStack().Count, "R2 修复转绿：停放重取不压第二个");
        }

        [Test]
        public void PayloadResidual_NoPayloadReopen_KeepsLastPayload_UntilOverwritten()
        {
            var p1 = new ProbeDto { Value = 1, Text = "P1" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "SlotResidual", null, in p1);
            var window = (SlotProbeWindow)_ledger.GetWindow("SlotResidual");
            _panels.Add(window.gameObject);
            Assert.AreEqual(1, window.Payload.Value, "量具前提坏了：先落 P1");

            _ledger.CloseUI<SlotProbeWindow>("SlotResidual");
            _ledger.ShowUIImp(typeof(SlotProbeWindow), false, "SlotResidual", null, UIPayload.Empty);

            Assert.AreEqual(1, window.Payload.Value, "关闭不清、无载荷腿不覆盖：残留仍是 P1");
            Assert.AreEqual("P1", window.Payload.Text, "关闭不清、无载荷腿不覆盖：残留仍是 P1");

            var p2 = new ProbeDto { Value = 2, Text = "P2" };
            _ledger.ShowUIImp<ProbeDto>(typeof(SlotProbeWindow), false, "SlotResidual", null, in p2);

            Assert.AreEqual(2, window.Payload.Value, "再开覆盖为 P2：载荷每次开窗覆盖");
            Assert.AreEqual("P2", window.Payload.Text, "再开覆盖为 P2：载荷每次开窗覆盖");
            Assert.AreSame(window, _ledger.GetWindow("SlotResidual"), "残留判据吃的是同一个缓存实例");
        }

        [Test]
        public void PayloadLastWins_StackedReopen_OverwritesPreviousPayload()
        {
            var p1 = new object();
            var p2 = new object();
            _ledger.ShowUIImp<object>(typeof(SlotRefProbeWindow), false, "SlotLastWins", null, in p1);
            _ledger.ShowUIImp<object>(typeof(SlotRefProbeWindow), false, "SlotLastWins", null, in p2);

            var window = (SlotRefProbeWindow)_ledger.GetWindow("SlotLastWins");
            _panels.Add(window.gameObject);
            Assert.AreSame(p2, window.Payload, "同一次开窗的第二次调用覆盖为 P2（last-wins）");
            Assert.AreEqual(1, _ledger.PeekStack().Count, "复用支路不压第二个");
        }
    }
}
