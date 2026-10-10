using System;
using System.Threading;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 轨道描述符：一支后端在门面通用逻辑里的全部自述——认窗判据、有效性探针、Type 形入口的开窗实现与关停档位。
    /// </summary>
    /// <remarks>
    /// 门面只按这份描述枚举轨道（认轨分派、有效性、按档位收口），不认识任何具体后端：加一轨＝加一个自登记的 <c>UIService.&lt;轨&gt;.cs</c> partial，主文件零改动。 <br />
    /// 轨道的「在位」由各轨自己的探针回答（探针读本轨槽位），目录不另记一份在位状态。 <br />
    /// 关停回调实例绑定、摘槽之后仍然有效：门面先摘槽再收口，摘槽只让窗口回叫静默，不撤掉还欠着的关停。 <br />
    /// 线程契约：仅主线程（登记走静态初始化，认领与摘除都在门面的生命周期里）。
    /// </remarks>
    internal sealed class UITrack
    {
        /// <summary>默认关停档：不持有别轨依赖的宿主资源，按目录序第一批收。</summary>
        internal const int SHUTDOWN_ORDER_DEFAULT = 0;

        /// <summary>宿主档：本轨持有别轨面板挂靠的宿主根（如 uGUI 的 UI 根），必须最后收。</summary>
        internal const int SHUTDOWN_ORDER_HOST = 100;

        private readonly string _trackName;
        private readonly Type _windowBaseType;
        private readonly int _shutdownOrder;
        private readonly Func<Type, bool> _ownsWindowType;
        private readonly Func<bool> _isDriverValid;
        private readonly Action<Type, bool, string, UIPayload, CancellationToken> _openWindow;
        private Action _claimedShutDown;

        /// <summary>轨道名：进抬错文案，点名是哪一轨。</summary>
        internal string TrackName => _trackName;

        /// <summary>本轨的窗口基类：进「认不出轨」的枚举文案，调用方照它挑要继承的基类。</summary>
        internal Type WindowBaseType => _windowBaseType;

        /// <summary>关停档位：门面按升序收口，越大越晚收；同档按登记序。</summary>
        internal int ShutdownOrder => _shutdownOrder;

        /// <summary>本轨驱动者是否在位：探针读的是那一轨自己的槽位。</summary>
        internal bool IsDriverValid => _isDriverValid();

        /// <summary>认领进槽时挂上的那条关停回调：实例绑定，摘槽不清它，门面整批清认领时才摘。</summary>
        internal Action ClaimedShutDown => _claimedShutDown;

        /// <summary>
        /// 造一条轨道自述。
        /// </summary>
        /// <param name="trackName">轨道名：进抬错文案。</param>
        /// <param name="windowBaseType">本轨的窗口基类：进「认不出轨」的枚举文案。</param>
        /// <param name="shutdownOrder">关停档位：升序收口，持有别轨宿主资源的轨取大档。</param>
        /// <param name="ownsWindowType">认窗判据：窗口类落在本轨窗口基类（含派生）之下时为真。</param>
        /// <param name="isDriverValid">有效性探针：读本轨自己的驱动者槽位，目录不另记一份。</param>
        /// <param name="openWindow">Type 形入口落到本轨的开窗实现。</param>
        internal UITrack(string trackName, Type windowBaseType, int shutdownOrder,
            Func<Type, bool> ownsWindowType, Func<bool> isDriverValid,
            Action<Type, bool, string, UIPayload, CancellationToken> openWindow)
        {
            _trackName = trackName;
            _windowBaseType = windowBaseType;
            _shutdownOrder = shutdownOrder;
            _ownsWindowType = ownsWindowType;
            _isDriverValid = isDriverValid;
            _openWindow = openWindow;
        }

        /// <summary>认窗判据：窗口类落在本轨的窗口基类（含派生）之下时为真。</summary>
        /// <param name="windowType">待判的窗口类型。</param>
        /// <returns>属于本轨时为真。</returns>
        internal bool OwnsWindowType(Type windowType) => _ownsWindowType(windowType);

        /// <summary>
        /// 把驱动者的关停交进轨道：认领门在槽位占上之后叫它，门面按档位升序收口时叫的也是它。
        /// </summary>
        /// <param name="driverShutDown">刚认领进槽的驱动者的关停。</param>
        internal void AttachDriver(Action driverShutDown) => _claimedShutDown = driverShutDown;

        /// <summary>整批清认领：归位门与关停收尾各走一次，下一轮认领重新挂。</summary>
        internal void ClearClaim() => _claimedShutDown = null;

        /// <summary>
        /// Type 形入口的开窗实现：泛型腿各叫自己那一轨的私有实现，Type 形入口经目录分派到这里。
        /// </summary>
        /// <param name="type">窗口类。</param>
        /// <param name="isAsync">面板按异步装载还是同步装载。</param>
        /// <param name="windowId">窗口标识（配置表 configId，或 Resources 目录下的相对路径）。</param>
        /// <param name="payload">动态腿擦除后的载荷。</param>
        /// <param name="ct">调用方取消令牌。</param>
        internal void OpenWindow(Type type, bool isAsync, string windowId,
            UIPayload payload, CancellationToken ct)
        {
            _openWindow(type, isAsync, windowId, payload, ct);
        }
    }
}
