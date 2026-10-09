using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.UIElements;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI Toolkit 轨窗口基类：面板实现（壳 GameObject/UIDocument/内容根）后端专有，对象模型不认这些类型。
    /// </summary>
    /// <remarks>
    /// 覆写 <see cref="UIWindow"/> 的七个面板钩子，把显隐/深度/交互三份意图落进真实的 UI Toolkit 面板；<br />
    /// 每项怎么落、边界在哪写在它自己的文档上（<see cref="ApplyVisible"/>、<see cref="ApplyDepth"/>、<see cref="ApplyInteractable"/>）。<br />
    /// 壳物体与 <see cref="UIDocument"/> 一窗一个；面板配置优先 <c>PanelSettingsOverride</c>，缺位回 <c>SharedPanelSettings</c>。<br />
    /// 未绑定时的空引用口径与 uGUI 轨同形：读 <c>gameObject</c> 回 null，读 <c>transform</c> 抛 <see cref="NullReferenceException"/>，<br />
    /// 三份意图的写入与 <see cref="ApplySafeInsets"/> 则是不落任何一笔的空操作。线程契约：仅主线程。
    /// </remarks>
    // ReSharper disable once InconsistentNaming
    public abstract class UITKWindow : UIWindow
    {
        #region 面板 [PANEL]

        private GameObject _shell;
        private UIDocument _document;
        private VisualElement _contentRoot;

        /// <summary>窗口位置组件。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override Transform transform => _shell.transform;

        /// <summary>窗口矩阵位置组件。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override RectTransform rectTransform => _shell.transform as RectTransform;

        /// <summary>窗口的实例资源对象（承载 <see cref="UIDocument"/> 的壳物体）。</summary>
        /// <remarks>保证与 Mono 的命名一致，沿袭使用习惯</remarks>
        public override GameObject gameObject => _shell;

        /// <summary>
        /// 本窗口的内容根：三份意图、安全区 inset 与克隆出来的模板都落在这里，不随文档组件的启用时机变化。
        /// </summary>
        /// <remarks>未绑定面板时为 <c>null</c>；它不是 <c>UIDocument.rootVisualElement</c>，而是挂在根元素之下的本窗口独占子树。</remarks>
        public VisualElement RootVisual => _contentRoot;

        /// <summary>壳物体上的文档组件（面板的排序归属）。</summary>
        /// <remarks>未绑定面板时为 <c>null</c>；后端按它读写本窗口的序。</remarks>
        internal UIDocument Document => _document;

        #endregion

        #region 后端配置 [BACKEND CONFIG]

        /// <summary>
        /// 全后端共享的那一份 <see cref="PanelSettings"/>：由项目侧写入（样式表与主题同样归项目侧承担），是本轨没有窗口级配置时的兜底。
        /// </summary>
        /// <remarks>
        /// 写口是 UI Toolkit 后端的资产引用，因此不住在中性的 <see cref="UIServiceSettings"/> 里，只在这一处对包外开口。 <br />
        /// 一窗一档的落点是 <see cref="PanelSettingsOverride"/>：它在场时这一位就让位，两者都不再是「全后端只有一份」。 <br />
        /// 两者都未写入时 <see cref="LoadPanel"/> / <see cref="LoadPanelAsync"/> 一律当场拒开并报一条 Error： <br />
        /// 不拿 <c>CreateInstance</c> 兜底——缺配置的窗口会拿到一份没有主题、没有缩放模式的裸面板，比拒开更难查。
        /// </remarks>
        public static PanelSettings SharedPanelSettings { get; set; }

        /// <summary>本窗口自己的 <see cref="PanelSettings"/>：装载本窗的面板时用它，为空时才回 <see cref="SharedPanelSettings"/>。</summary>
        /// <remarks>
        /// 来路两条：UI Toolkit 腿带 <c>panelSettings</c> 实参时，由本轨在开窗支路里、面板装载之前经交接钩子写进来；项目侧也可以在自己窗口类的构造里直接给。 <br />
        /// 只在造出新实例那一档被交到：栈上复用与停放重取那两条支路里面板早已装好，它与面板地址一样不再吃。 <br />
        /// 判「有没有」用 Unity 的 == 而不是 <c>??</c>：共享那一份是资产引用，会被从背后销毁，<c>??</c> 认不出那种假空。 <br />
        /// 线程契约：仅主线程（与装载同一条线程）。
        /// </remarks>
        public PanelSettings PanelSettingsOverride { get; set; }

        /// <summary>本窗装载时实际交给文档组件的那一份 <see cref="PanelSettings"/>：窗口级覆盖优先，缺位时回共享兜底。</summary>
        private PanelSettings ResolvedPanelSettings =>
            PanelSettingsOverride != null ? PanelSettingsOverride : SharedPanelSettings;

        #endregion

        #region 面板钩子 [PANEL HOOKS]

        /// <summary>装载面板：按 <paramref name="fromResources"/> 走 AB 或内置资源取模板，再建壳交给自己装配。</summary>
        protected internal override bool LoadPanel(string assetLocation, bool fromResources)
        {
            if (!TryReadTree(assetLocation, fromResources, out var tree))
            {
                return false;
            }

            return BindPanel(NewDocumentShell(), tree);
        }

        /// <summary>装载面板（异步）：AB 路径 await 模板，内置资源那一路仍走同步 <c>Resources.Load</c>。</summary>
        /// <remarks>配置与模板的判据同同步路径：任一项缺位都只报一条 Error 并回 false，不建壳、不装配。</remarks>
        protected internal override async UniTask<bool> LoadPanelAsync(string assetLocation, bool fromResources, CancellationToken ct)
        {
            if (fromResources)
            {
                return LoadPanel(assetLocation, true);
            }

            if (!EnsurePanelSettings(assetLocation))
            {
                return false;
            }

            var tree = await ResourceService.TryLoadAssetAsync<VisualTreeAsset>(assetLocation, ct);
            if (tree == null)
            {
                LogUtility.Error("UI Toolkit 窗口 '{0}' 取不到面板模板 {1}", WindowId, assetLocation);
                return false;
            }

            return BindPanel(NewDocumentShell(), tree);
        }

        /// <summary>显隐落地：切内容根的 <c>style.display</c>；同值早退只认「这槽真被写过」，没写过的槽一律先落一笔。</summary>
        /// <remarks>
        /// <c>DisplayStyle.Flex</c> 是 0 值：没写过 <c>display</c> 的内容根读回的 <c>value</c> 就是它，只比 <c>value</c> 会被初值冒充成已到位。<br />
        /// 一次都不写，窗口显隐意图在这棵树上没留过痕迹，模板样式表按名字选择器置的 <c>none</c> 也没东西压回去。<br />
        /// 存在性看 <c>keyword</c>：停在 <c>StyleKeyword.Null</c> 才是这一槽压根没被碰过。
        /// </remarks>
        protected internal override void ApplyVisible(bool value)
        {
            if (_contentRoot == null) return;

            var set = value ? DisplayStyle.Flex : DisplayStyle.None;
            var current = _contentRoot.style.display;
            if (current.keyword != StyleKeyword.Null && current.value == set) return;

            _contentRoot.style.display = set;
        }

        /// <summary>深度落地：文档组件取绝对值；UI Toolkit 的序不住在子元素上，没有偏移差分要保。</summary>
        protected internal override void ApplyDepth(int value)
        {
            if (_document == null) return;

            if (_document.sortingOrder == value) return;

            _document.sortingOrder = value;
        }

        /// <summary>交互落地：只切内容根自身的 <c>pickingMode</c>。</summary>
        /// <remarks>
        /// <c>pickingMode</c> 是逐元素属性：内容根置 <c>Ignore</c> 只让自己退出命中树，屏蔽不到克隆进来的模板内容与代码追加的子元素。<br />
        /// 刻意不递归铺锁：解锁时分不出「本来就 <c>Ignore</c> 的装饰元素」与被锁元素，一刀切回 <c>Position</c> 会打穿按元素的意图。
        /// </remarks>
        protected internal override void ApplyInteractable(bool value)
        {
            if (_contentRoot == null) return;

            _contentRoot.pickingMode = value ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>停放面板：壳物体留着但不激活（缓存实例的关闭态、关闭动画结束后的隐藏）。</summary>
        protected internal override void ParkPanel()
        {
            _shell.SetActive(false);
        }

        /// <summary>收走面板：销毁壳物体并断开全部后端引用，令窗口回到「未绑定」口径。</summary>
        protected internal override void DestroyPanel()
        {
            if (_shell != null)
            {
                UObject.Destroy(_shell);
                _shell = null;
            }

            // 引用一并断开：装载完成前窗口已被销毁时，本类压根没装配过面板，后续写意图必须同样落空
            _document = null;
            _contentRoot = null;
        }

        #endregion

        #region 装载与装配 [ASSEMBLY]

        /// <summary>
        /// 接管一块 UI Toolkit 面板：校验壳上的 <see cref="UIDocument"/>、按窗口类型名改写壳与内容根、克隆模板、贴主题并挂进文档根元素。
        /// </summary>
        /// <param name="shell">面板壳物体；null 时直接判装载失败。</param>
        /// <param name="tree">面板模板资产；null 表示内容由代码构建，只建空内容根。</param>
        /// <returns>装配成功返回 true。</returns>
        /// <remarks>
        /// 校验先于写入：缺 <see cref="UIDocument"/> 时抛 <see cref="GameException"/>，此时一个字段都不动，调用方不得拿到半个可用面板。 <br />
        /// 内容根一律由本类 new 出来：三份意图与 inset 都写在它身上，与文档有没有把根元素建起来无关—— <br />
        /// <c>UIDocument.rootVisualElement</c> 由组件自身的启用流程创建，拿不到宿主时不做 attach，也不报错。
        /// </remarks>
        internal bool BindPanel(GameObject shell, VisualTreeAsset tree)
        {
            if (shell == null) return false;

            var document = shell.GetComponent<UIDocument>();
            if (document == null)
            {
                throw new GameException(StringUtility.Format(
                    "面板壳 {0}（窗口 {1}）上找不到 {2}：UI Toolkit 窗口的壳必须自带文档组件", shell.name, WindowId, nameof(UIDocument)));
            }

            _shell = shell;
            _document = document;
            _shell.name = GetType().Name;
            _shell.transform.localPosition = Vector3.zero;

            _contentRoot = new VisualElement();
            _contentRoot.name = GetType().Name;
            if (tree != null)
            {
                // 模板只在装配时克隆一次：资源层的取值带保活窗口，裸资产引用不长期留在窗口上
                tree.CloneTree(_contentRoot);
            }

            ApplyTheme();

            var host = _document.rootVisualElement;
            if (host != null)
            {
                host.Add(_contentRoot);
            }

            return true;
        }

        /// <summary>
        /// 建一个承载文档组件的壳物体：挂在 <see cref="UIService.UIRoot"/> 下，先把本窗的 <see cref="PanelSettings"/> 配好再放它进激活流程。
        /// </summary>
        /// <remarks>
        /// 壳建成即不激活：<see cref="UIDocument"/> 的根元素与面板都在启用流程里创建，配置晚一步就位它就按空配置报错。 <br />
        /// 写入的是 <see cref="ResolvedPanelSettings"/>：窗口级覆盖在场用覆盖，否则用共享那一份兜底——调用方给过覆盖时这里不得回读共享位。 <br />
        /// 排序初值归零：深度意图随后由 <see cref="UIWindow"/> 结算，装载前攒下的 <see cref="UIWindow.Depth"/> 当场覆盖这里。
        /// </remarks>
        private GameObject NewDocumentShell()
        {
            var shell = new GameObject(GetType().Name, typeof(RectTransform));
            shell.SetActive(false);
            shell.transform.SetParent(UIService.UIRoot, false);

            var document = shell.AddComponent<UIDocument>();
            document.panelSettings = ResolvedPanelSettings;
            document.sortingOrder = 0;

            shell.SetActive(true);
            return shell;
        }

        /// <summary>取面板模板：内置资源走 <c>Resources</c>，其余走资源服务的按地址取值。</summary>
        /// <remarks>两条判据各自只报一条 Error：配置缺位与模板缺位是两种病因，文案分开才认得出是哪一种。</remarks>
        private bool TryReadTree(string assetLocation, bool fromResources, out VisualTreeAsset tree)
        {
            tree = null;
            if (!EnsurePanelSettings(assetLocation))
            {
                return false;
            }

            if (fromResources)
            {
                tree = Resources.Load<VisualTreeAsset>(assetLocation);
            }
            else
            {
                ResourceService.TryLoadAsset(assetLocation, out tree);
            }

            if (tree == null)
            {
                LogUtility.Error("UI Toolkit 窗口 '{0}' 取不到面板模板 {1}", WindowId, assetLocation);
                return false;
            }

            return true;
        }

        /// <summary>校验本窗这一档的 <see cref="PanelSettings"/> 已配置：窗口级覆盖与共享兜底任一在场即可。</summary>
        /// <remarks>
        /// 两者都缺位才抬这一条 Error，且只报一次、回 false：它与「取不到面板模板」是两种病因，文案各自分开才认得出是哪一种。 <br />
        /// 覆盖为空不是错：那正是回 <see cref="SharedPanelSettings"/> 的那一档，只有两处都空才是配置缺位。
        /// </remarks>
        /// <param name="assetLocation">要装载的面板地址，只进文案。</param>
        /// <returns>本窗拿得到一份面板配置时为真。</returns>
        private bool EnsurePanelSettings(string assetLocation)
        {
            if (ResolvedPanelSettings != null)
            {
                return true;
            }

            LogUtility.Error("UI Toolkit 后端未配置 {0}（{1} 与 {2} 都为空）：拒绝装载面板 {3}",
                nameof(PanelSettings), nameof(PanelSettingsOverride), nameof(SharedPanelSettings), assetLocation);
            return false;
        }

        /// <summary>本窗口的主题样式表定位地址；为空表示不吃主题。</summary>
        /// <remarks>样式表本身由项目侧决定，框架只负责在装配时把它挂进内容根。</remarks>
        protected virtual string ThemeLocation => null;

        /// <summary>贴主题：<see cref="ThemeLocation"/> 声明了才取样式表，取到就挂进内容根自己的 <c>styleSheets</c>。</summary>
        /// <remarks>
        /// 只降级不拒开：主题缺失报一条 Error 后面板照旧可用。不动 <see cref="PanelSettings.themeStyleSheet"/>——那是共享资产， <br />
        /// 一个窗口改它会影响全部 UI Toolkit 窗口。
        /// </remarks>
        private void ApplyTheme()
        {
            var location = ThemeLocation;
            if (string.IsNullOrEmpty(location))
            {
                return;
            }

            if (!ResourceService.TryLoadAsset(location, out StyleSheet sheet) || sheet == null)
            {
                LogUtility.Error("UI Toolkit 窗口 '{0}' 取不到主题样式表 {1}", WindowId, location);
                return;
            }

            _contentRoot.styleSheets.Add(sheet);
        }

        #endregion

        #region 安全区适配 [SAFE AREA]

        /// <summary>
        /// 把安全区 inset 写成内容根的四条 padding。
        /// </summary>
        /// <param name="left">左边距。</param>
        /// <param name="top">上边距。</param>
        /// <param name="right">右边距。</param>
        /// <param name="bottom">下边距。</param>
        /// <remarks>
        /// 取值由调用方给（uGUI 轨的适配读 <c>rectTransform</c>，UI Toolkit 的内容根没有矩形可读）；未绑定时判为空操作。 <br />
        /// 写的是局部样式，父级的相对单位与布局由 UI Toolkit 自己算，本类不回读实际像素。
        /// </remarks>
        internal void ApplySafeInsets(float left, float top, float right, float bottom)
        {
            if (_contentRoot == null) return;

            _contentRoot.style.paddingLeft = left;
            _contentRoot.style.paddingTop = top;
            _contentRoot.style.paddingRight = right;
            _contentRoot.style.paddingBottom = bottom;
        }

        #endregion
    }

    /// <summary>UI Toolkit 轨带载荷窗口基类：每次开窗最多一个强类型 DTO，静态腿泛型直塞（struct 不装箱）。</summary>
    /// <remarks>载荷每次开窗覆盖、关闭不清；再开覆盖。动态腿经 <see cref="UIPayload"/> 擦除后从这里取回。</remarks>
    // ReSharper disable once InconsistentNaming
    public abstract class UITKWindow<TArg> : UITKWindow, IUIPayloadSlot<TArg>
    {
        /// <summary>本次开窗的载荷。</summary>
        public TArg Payload { get; private set; }

        void IUIPayloadSlot<TArg>.SetPayload(in TArg payload) => Payload = payload;

        internal override void Internal_SetPayload(UIPayload payload) => Payload = payload.To<TArg>();
    }
}
