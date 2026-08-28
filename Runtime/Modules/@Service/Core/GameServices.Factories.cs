using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认服务工厂表（<see cref="GameServices"/> partial）。
    /// <para>框架内置服务的默认实例来源——依赖预注册链中未显式注册的依赖由此创建；
    /// 宿主工程可通过 <see cref="RegisterDefaultFactory"/> 为自有服务贡献工厂，
    /// 使 <c>[ServiceDependency]</c> 依赖链跨程序集自动装配。</para>
    /// <para>注册期专用路径，不在任何热路径上；工厂表随域重载重建，跨 Shutdown 持久（属配置而非运行状态）。</para>
    /// </summary>
    public static partial class GameServices
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 类型 → 默认实例工厂。初始化期一次性构建；static lambda 无闭包捕获。
        /// <para><b>internal 仅供测试程序集</b>做跨域隔离清理（关闭 Domain Reload 时静态表存活，
        /// 测试需自行快照并还原）；运行时一律经 <see cref="RegisterDefaultFactory"/> 写入。</para>
        /// </summary>
        internal static readonly Dictionary<Type, Func<IService>> s_DefaultFactories = new()
        {
            [typeof(UpdateDriver.UpdateDriverService)] = static () => new UpdateDriver.UpdateDriverService(),
            [typeof(Resource.ResourceService)] = static () => new Resource.ResourceService(),
            [typeof(Debugger.DebuggerService)] = static () => new Debugger.DebuggerService(),
            [typeof(Audio.AudioService)] = static () => new Audio.AudioService(),
            [typeof(ObjectPool.ObjectPoolService)] = static () => new ObjectPool.ObjectPoolService(),
            [typeof(Procedure.ProcedureService)] = static () => new Procedure.ProcedureService(),
            [typeof(Localization.LocalizationService)] = static () => new Localization.LocalizationService(),
            [typeof(Scene.SceneService)] = static () => new Scene.SceneService(),
            [typeof(Timer.TimerService)] = static () => new Timer.TimerService(),
            [typeof(Save.SaveService)] = static () => new Save.SaveService(),
            [typeof(UI.UIService)] = static () => new UI.UIService(),
            [typeof(Input.InputService)] = static () => new Input.InputService(),
            [typeof(ConfigTable.ConfigTableService)] = static () => new ConfigTable.ConfigTableService(),
        };

        #endregion

        #region 公共扩展点 [PUBLIC EXTENSION POINT]

        /// <summary>
        /// 注册服务默认实例工厂。
        /// <para>宿主工程为自有服务贡献工厂后，其他服务以 <c>[ServiceDependency(typeof(...))]</c>
        /// 声明该服务时即可自动递归预注册，无需手动控制注册顺序。</para>
        /// <para>同一类型重复注册抛 <see cref="GameException"/>（fail-fast）；表内容跨 Shutdown 持久。</para>
        /// </summary>
        /// <param name="serviceType">服务具体类型（必须实现 <see cref="IService"/>）。</param>
        /// <param name="factory">实例工厂。</param>
        public static void RegisterDefaultFactory(Type serviceType, Func<IService> factory)
        {
            EnsureMainThread();
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!typeof(IService).IsAssignableFrom(serviceType))
            {
                throw new ArgumentException(StringUtility.Format(
                    "Default factory type '{0}' does not implement IService.", serviceType.FullName),
                    nameof(serviceType));
            }

            if (s_DefaultFactories.ContainsKey(serviceType))
            {
                throw new GameException(StringUtility.Format(
                    "Default factory for service '{0}' is already registered.",
                    serviceType.FullName));
            }

            s_DefaultFactories[serviceType] = factory;
        }

        #endregion

        #region 工厂解析 [FACTORY RESOLUTION]

        /// <summary>
        /// 创建服务默认实例——依赖预注册的实例来源。
        /// </summary>
        private static IService CreateDefaultService(Type serviceType)
        {
            if (s_DefaultFactories.TryGetValue(serviceType, out var factory))
                return factory();

            throw new GameException(StringUtility.Format(
                "Service '{0}' is not registered and has no default factory. Register it explicitly or call RegisterDefaultFactory before its dependents.",
                serviceType.FullName));
        }

        #endregion
    }
}
