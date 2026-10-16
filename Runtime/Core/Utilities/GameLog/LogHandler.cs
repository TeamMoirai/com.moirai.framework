using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架日志处理器基类（策略模式抽象策略），后端经 <see cref="LogUtility.Handler"/> 替换。
    /// </summary>
    /// <remarks>
    /// 内置实现只有 <see cref="DefaultLogHandler"/>（<c>UnityEngine.Debug</c>，始终可用，默认）；
    /// 其余后端（Unity Logging、Serilog、ZLogger 等）由 <c>plugin</c> 分支的插件各自装进来——未安装对应三方包时那些类型根本不存在，
    /// 本装配也不提它们。跨程序集的覆写须声明为 <c>protected</c>，
    /// 覆写 <c>protected internal</c> 成员不能收窄成 <c>internal</c>。
    /// </remarks>
    [Serializable]
    public abstract class LogHandler : FrameworkHandler
    {
        [Tooltip("最小日志等级，低于该等级的日志将被丢弃。")]
        [SerializeField] private ELogLevel m_MinimumLevel = ELogLevel.Verbose;
        /// <summary>获取或设置最小日志等级，低于该等级的日志将被丢弃。</summary>
        protected internal ELogLevel MinimumLevel
        {
            get
            {
#if !UNITY_EDITOR
                if (m_MinimumLevel < ELogLevel.Info) return ELogLevel.Info;
#endif
                return m_MinimumLevel;
            }
            set => m_MinimumLevel = value;
        }

        [Space]
        [SerializeField] private bool m_TimestampEnabled;
        [ShowIf(nameof(m_TimestampEnabled))]
        [SerializeField] private string m_TimestampFormat = "HH:mm:ss.fff";

        /// <summary>获取或设置是否在日志输出中包含时间戳。</summary>
        /// <remarks>
        /// 各实现经后端自身的模板/格式化系统应用：<see cref="DefaultLogHandler"/> 在消息前缀拼接 <c>[HH:mm:ss.fff]</c>；
        /// <c>plugin</c> 分支的外置后端各自处置——Unity Logging 与 Serilog 由后端 outputTemplate 的 <c>{Timestamp}</c> 占位符控制，
        /// ZLogger 由自己的 formatter 模板提供。
        /// </remarks>
        public bool TimestampEnabled
        {
            get => m_TimestampEnabled;
            set => m_TimestampEnabled = value;
        }

        /// <summary>获取或设置时间戳格式字符串（默认 <c>HH:mm:ss.fff</c>）。</summary>
        public string TimestampFormat
        {
            get => m_TimestampFormat;
            set => m_TimestampFormat = value;
        }

        /// <summary>获取当前时间戳前缀字符串（含尾部空格），未启用时返回 null。</summary>
        protected string TimestampPrefix
            => m_TimestampEnabled ? StringUtility.Format("[{0}] ", DateTime.Now.ToString(m_TimestampFormat)) : null;

        /// <summary>
        /// 获取可直写 Unity 控制台、绕过全局拦截器的 logHandler，后端输出通道须经它取用（详见 <see cref="LogUtility.GetBypassUnityHandler"/>）。
        /// </summary>
        protected internal static ILogHandler BypassUnityHandler => LogUtility.GetBypassUnityHandler();

        /// <inheritdoc/>
        protected override void OnInit()
        {
            base.OnInit();

            LogUtility.EnableGlobalInterception();
        }

        /// <summary>
        /// 带异常对象的 Error 条目是否经 Unity 的 LogException 通道呈现（UTF 因此把它当作 <c>LogType.Exception</c> 而非 <c>LogType.Error</c>）。
        /// </summary>
        /// <remarks>
        /// 基类默认 <c>false</c>，与 <see cref="DefaultLogHandler"/> 一致：Error 落 <c>LogType.Error</c>，只有 Fatal 带异常时才走 LogException。 <br />
        /// 后端只要「条目自带异常就升级成 Exception」即覆盖为 <c>true</c>（<c>plugin</c> 分支的 zlogger 插件属此）。 <br />
        /// 这条判据存在的理由：日志级别随处理器变，而测试装配看不到各后端的 <c>*_INSTALLED</c> 宏，把级别写死在用例里换一次处理器就假红。
        /// </remarks>
        public virtual bool ErrorWithExceptionUsesExceptionChannel => false;

        /// <summary>
        /// 记录一条已格式化的日志。
        /// </summary>
        /// <param name="logLevel">游戏框架日志等级。</param>
        /// <param name="message">已格式化的日志内容，不为 null。</param>
        /// <param name="exception">关联异常，无异常时为 null，由各实现决定是否输出异常堆栈。</param>
        /// <param name="context">日志关联对象（可选，Console 点击可定位到该对象）。</param>
        protected internal abstract void Log(ELogLevel logLevel, string message, [CanBeNull] Exception exception, UObject context = null);

        #region 日志等级 [LOG LEVEL]

        /// <summary>
        /// 游戏框架日志等级。
        /// </summary>
        /// <remarks>嵌在处理器基类内：等级词表只对后端（含 <c>plugin</c> 分支的插件）开放，不作为框架的公共类型对外暴露。</remarks>
        protected internal enum ELogLevel : byte
        {
            /// <summary>最详细的日志，可能包含敏感数据（如参数值、令牌等）。</summary>
            /// <example>仅在开发环境临时调试，或生产环境排查极难复现的问题时临时开启</example>
            /// <remarks>默认关闭，绝不能在生产环境长期启用，否则有安全风险</remarks>
            Verbose = 0,

            /// <summary>用于交互式调试，记录变量状态、方法进入/退出等。</summary>
            /// <example>开发人员在本地开发时使用，辅助定位逻辑错误</example>
            /// <remarks>无长期保留价值，生产环境通常关闭（除非临时调试）</remarks>
            Debug,

            /// <summary>记录应用程序的常规流程，如服务启动、用户登录、业务操作完成。</summary>
            /// <example>生产环境推荐的最小日志级别，用于跟踪系统运行状态和审计</example>
            /// <remarks>应具有长期保留价值，帮助运维人员了解系统行为</remarks>
            Info,

            /// <summary>发生异常或意外情况，但不影响当前操作继续执行（如降级处理、重试成功）。</summary>
            /// <example>记录非致命问题，便于提前预警，如磁盘空间不足、请求稍慢</example>
            /// <remarks>表明系统仍可用，但需关注潜在风险</remarks>
            Warning,

            /// <summary>当前请求/活动因失败而中断，但不影响整个应用进程（如 HTTP 500、数据库连接失败）。</summary>
            /// <example>记录业务错误或组件故障，需开发人员介入排查</example>
            /// <remarks>这是最常见的“报错”级别，重点监控</remarks>
            Error,

            /// <summary>灾难性故障，应用或系统崩溃、数据丢失风险，需立即人工干预。</summary>
            /// <example>如无法恢复的异常、关键服务宕机、数据库不可用</example>
            /// <remarks>应当触发警报，通知值班人员</remarks>
            Fatal
        }

        #endregion
    }
}
