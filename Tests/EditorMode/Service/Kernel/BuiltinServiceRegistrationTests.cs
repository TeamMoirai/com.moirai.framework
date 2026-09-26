using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace Service.Kernel
{
    /// <summary>
    /// 内置服务自动注册（[AutoRegisterService] + BuiltinServiceRegistrationGenerator）回归锁。
    /// <para>① 生成清单 <c>BuiltinServiceRegistration.RegisterAll</c> 注册集合与程序集内标记集合严格相等；</para>
    /// <para>② 框架程序集内全部可实例化的非 MonoBehaviour <see cref="IService"/> 实现必须带标记——
    /// 漏标记即回归（组合根不再持有手写清单，漏标服务将永远不会注册）。</para>
    /// </summary>
    [TestFixture]
    public class BuiltinServiceRegistrationTests
    {
        private static readonly Assembly s_FrameworkAssembly = typeof(GameApp).Assembly;

        [Test]
        public void RegisterAll_RegistersExactlyMarkedTypes()
        {
            var marked = CollectMarkedTypes();

            Assert.Greater(marked.Count, 0, "程序集内未发现任何 [AutoRegisterService] 标记——生成器或特性失效");

            var world = new ServiceWorld();
            // 故意不 Dispose：隔离世界持有的是未初始化的真实服务实例，
            // Dispose 会对未激活服务兜底驱动 OnShutdown（ServiceScope.DisposeInternal），无必要引入副作用；
            // 纯托管对象图交由 GC 回收即可。
            BuiltinServiceRegistration.RegisterAll(world);

            foreach (var pair in marked)
            {
                Assert.IsTrue(world.TryGetScope(pair.Key, out var scope),
                    $"作用域未创建：{pair.Key}（服务 {pair.Value.FullName}）");
                Assert.IsTrue(scope.TryGet(pair.Value, out var service),
                    $"标记服务未注册：{pair.Value.FullName}");
                Assert.IsInstanceOf(pair.Value, service,
                    $"注册实例类型不符：期望 {pair.Value.FullName}，实际 {service.GetType().FullName}");
            }

            // 反向校验：各作用域注册总数与标记数相等（无多注册/重复展开）
            var infos = new List<GameServices.DiagnosticInfo>();
            world.CollectDiagnosticInfo(infos);

            var expectedPerScope = new Dictionary<EServiceScopeKind, int>();
            foreach (var pair in marked)
            {
                expectedPerScope.TryGetValue(pair.Key, out int count);
                expectedPerScope[pair.Key] = count + 1;
            }

            foreach (var info in infos)
            {
                Assert.IsTrue(expectedPerScope.TryGetValue(info.Scope, out _),
                    $"出现了标记集合之外的作用域注册：{info.ContractType} @ {info.Scope}");
                expectedPerScope[info.Scope]--;
            }

            foreach (var pair in expectedPerScope)
            {
                Assert.AreEqual(0, pair.Value,
                    $"作用域 {pair.Key} 注册数量与标记数量不一致（差值 {pair.Value}）");
            }
        }

        [Test]
        public void BuiltinServices_AllInstantiableServices_AreMarked()
        {
            var violators = new List<string>();

            foreach (var type in s_FrameworkAssembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                if (!typeof(IService).IsAssignableFrom(type))
                    continue;
                // Scene/Gameplay 的 MonoBehaviour 服务（ServiceMono）走 Awake 自注册，不参与自动注册清单
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                    continue;
                if (type.GetCustomAttribute<AutoRegisterServiceAttribute>() != null)
                    continue;

                violators.Add(type.FullName);
            }

            Assert.IsEmpty(violators,
                "以下可实例化服务缺少 [AutoRegisterService] 标记（组合根已改为生成清单，漏标即漏注册）："
                + string.Join(", ", violators));
        }

        private static List<KeyValuePair<EServiceScopeKind, Type>> CollectMarkedTypes()
        {
            var result = new List<KeyValuePair<EServiceScopeKind, Type>>();

            foreach (var type in s_FrameworkAssembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<AutoRegisterServiceAttribute>();
                if (attr == null)
                    continue;

                result.Add(new KeyValuePair<EServiceScopeKind, Type>(attr.Scope, type));
            }

            return result;
        }
    }
}
