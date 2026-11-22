using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace Service.Kernel
{
    /// <summary>
    /// 组合根注册清单（BuiltinServiceRegistrationGenerator）的契约守卫。
    /// </summary>
    /// <remarks>
    /// 清单形态 = 纯服务后端预热：RegisterAll 只发射 <c>WarmHandler</c>（[AutoRegisterService] 实例注册已整体退役删除）。 <br />
    /// ① RegisterAll 注册的契约键恰好覆盖全部 [ProviderHandlerHost] 接口契约宿主，实现都是 ServiceBase 派生后端； <br />
    /// ② 漏注册审计——程序集内全部可实例化非 MonoBehaviour 服务必须落在服务后端豁免面内。
    /// </remarks>
    [TestFixture]
    public class BuiltinServiceRegistrationTests
    {
        private static readonly Assembly s_FrameworkAssembly = typeof(GameApp).Assembly;

        [TearDown]
        public void TearDown()
        {
            // RegisterAll 的预热把 settings handler 回填进缓存槽并注册进默认世界；本测试不消费槽位，
            // 关掉世界让兜底销毁驱动失效钩子清槽，避免跨夹具残留（EditMode 同域共享静态态）。
            GameServices.Shutdown();
        }

        [Test]
        public void RegisterAll_WarmsExactlyProviderContractHosts()
        {
            // WarmHandler 走默认世界（GameServices 静态投影）：先复位到全新未初始化世界，
            // 保证服务后端预热走「先入图」路径，而不是对已初始化世界做运行时依赖校验
            GameServices.Shutdown();

            var providerContracts = CollectProviderContractInterfaces();
            Assert.Greater(providerContracts.Count, 0,
                "程序集内未发现任何 [ProviderHandlerHost] 接口契约宿主——特性或收集失效");

            // warm 链经 GameServices.RegisterService 静态投影注册，目标恒为默认世界——
            // 隔离世界收不到预热产物（生成代码的组合根语义），诊断必须对 Default 收集
            var world = GameServices.Default;
            BuiltinServiceRegistration.RegisterAll(world);

            var infos = new List<GameServices.DiagnosticInfo>();
            world.CollectDiagnosticInfo(infos);

            var registeredContracts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in infos)
            {
                Assert.IsTrue(providerContracts.TryGetValue(Type.GetType(info.ContractType) ?? FindTypeInLoadedAssemblies(info.ContractType), out _),
                    $"出现了契约宿主清单之外的作用域注册：{info.ContractType} @ {info.Scope}");
                registeredContracts.Add(info.ContractType);

                // 实现必须是 ServiceBase 派生的服务后端（绑定即注册，无裸实例注册）
                var impl = typeof(ServiceBase).Assembly.GetType(info.ImplementationType)
                    ?? FindTypeInLoadedAssemblies(info.ImplementationType);
                Assert.IsTrue(impl != null && typeof(ServiceBase).IsAssignableFrom(impl),
                    $"契约 {info.ContractType} 绑定的实现不是 ServiceBase 派生后端：{info.ImplementationType}");
            }

            foreach (var contract in providerContracts)
            {
                Assert.IsTrue(registeredContracts.Contains(contract.FullName),
                    $"契约宿主未被预热注册：{contract.FullName}");
            }
        }

        [Test]
        public void BuiltinServices_AllInstantiableServices_AreRegisteredBackends()
        {
            // 本守卫：程序集内全部可实例化的非 MonoBehaviour IService 实现，必须全部落在豁免面内——
            // 服务后端（契约接口实现 / UI 家族基座派生）由 settings 选型或 UI 认轨注册，不走程序集扫描。
            var providerContracts = CollectProviderContractInterfaces();
            var violators = new List<string>();

            foreach (var type in s_FrameworkAssembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                if (!typeof(IService).IsAssignableFrom(type))
                    continue;
                // Scene/Gameplay 的 MonoBehaviour 服务（ServiceMono）走 Awake 自注册，不参与组合根清单
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                    continue;
                // 豁免 1：实现某 ProviderHandlerHost 契约接口的 ServiceBase 派生类（settings 选型驱动）
                if (IsServiceHandlerBackend(type, providerContracts))
                    continue;
                // 豁免 2：UI 双轨家族基座派生类（UIService.RegisterEnabledHandlers 认轨注册，无 ProviderHandlerHost 绑定）
                if (IsUiTrackHandler(type))
                    continue;

                violators.Add(type.FullName);
            }

            Assert.IsEmpty(violators,
                "以下可实例化服务不在服务后端豁免面内（漏注册）："
                + string.Join(", ", violators));
        }

        /// <summary>UI 双轨后端判据：UIServiceHandler 家族基座（ServiceBase 派生）的派生类。</summary>
        private static bool IsUiTrackHandler(Type type)
        {
            var uiFamily = typeof(Moirai.Atropos.UI.IUIService).Assembly.GetType("Moirai.Atropos.UI.UIServiceHandler");
            return uiFamily != null && uiFamily.IsAssignableFrom(type);
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>全部 [ProviderHandlerHost] 接口契约宿主自报的契约接口类型集合。</summary>
        private static HashSet<Type> CollectProviderContractInterfaces()
        {
            var contracts = new HashSet<Type>();
            foreach (var type in s_FrameworkAssembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<ProviderHandlerHostAttribute>();
                if (attr?.HandlerType == null || !attr.HandlerType.IsInterface)
                    continue;

                contracts.Add(attr.HandlerType);
            }

            return contracts;
        }

        /// <summary>服务后端判据：实现某宿主契约接口的 ServiceBase 派生类。</summary>
        private static bool IsServiceHandlerBackend(Type type, HashSet<Type> providerContracts)
        {
            if (!typeof(ServiceBase).IsAssignableFrom(type))
                return false;

            foreach (Type contract in providerContracts)
            {
                if (contract.IsAssignableFrom(type))
                    return true;
            }

            return false;
        }
    }
}
