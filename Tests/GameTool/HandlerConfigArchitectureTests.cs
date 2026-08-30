using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

using Res = Moirai.Atropos.Resource;
using Aud = Moirai.Atropos.Audio;
using Cfg = Moirai.Atropos.ConfigTable;
using Dbg = Moirai.Atropos.Debugger;
using Inp = Moirai.Atropos.Input;
using Loc = Moirai.Atropos.Localization;
using OPo = Moirai.Atropos.ObjectPool;
using Sav = Moirai.Atropos.Save;
using Scn = Moirai.Atropos.Scene;
using Tim = Moirai.Atropos.Timer;
using UIm = Moirai.Atropos.UI;

namespace GameTool
{
    /// <summary>
    /// Handler 配置架构回归锁：锁定「配置/行为分离」重构后的结构不变量。
    /// <para>核心目标：防止 Handler 实现类重新被序列化（快照状态污染复发的结构锁），
    /// 以及 Settings 字段、Config 工厂签名的契约回归。</para>
    /// </summary>
    public sealed class HandlerConfigArchitectureTests
    {
        // (服务契约类型, 默认实现类型, 默认配置类型)
        private static readonly (Type handler, Type impl, Type config)[] s_Services =
        {
            (typeof(Res.ResourceServiceHandler), typeof(Res.YooAssetHandler), typeof(Res.YooAssetHandlerConfig)),
            (typeof(Aud.AudioServiceHandler), typeof(Aud.UnityAudioHandler), typeof(Aud.UnityAudioHandlerConfig)),
            (typeof(Cfg.ConfigTableServiceHandler), typeof(Cfg.DefaultConfigTableHandler), typeof(Cfg.DefaultConfigTableHandlerConfig)),
            (typeof(Dbg.DebuggerServiceHandler), typeof(Dbg.DefaultDebuggerHandler), typeof(Dbg.DefaultDebuggerHandlerConfig)),
            (typeof(Inp.InputServiceHandler), typeof(Inp.UnityInputSystemHandler), typeof(Inp.UnityInputSystemHandlerConfig)),
            (typeof(Loc.LocalizationServiceHandler), typeof(Loc.ConfigTableLocalizationHandler), typeof(Loc.ConfigTableLocalizationHandlerConfig)),
            (typeof(OPo.ObjectPoolServiceHandler), typeof(OPo.DefaultObjectPoolHandler), typeof(OPo.DefaultObjectPoolHandlerConfig)),
            (typeof(OPo.GameObjectPoolServiceHandler), typeof(OPo.DefaultGameObjectPoolHandler), typeof(OPo.DefaultGameObjectPoolHandlerConfig)),
            (typeof(Sav.SaveServiceHandler), typeof(Sav.JsonSaveHandler), typeof(Sav.JsonSaveHandlerConfig)),
            (typeof(Scn.SceneServiceHandler), typeof(Scn.DefaultSceneHandler), typeof(Scn.DefaultSceneHandlerConfig)),
            (typeof(Tim.TimerServiceHandler), typeof(Tim.DefaultTimerHandler), typeof(Tim.DefaultTimerHandlerConfig)),
            (typeof(UIm.UIServiceHandler), typeof(UIm.UGUIHandler), typeof(UIm.UGUIHandlerConfig)),
        };

        /// <summary>
        /// 结构锁 a：Handler 抽象契约与实现类均不得标注 [Serializable]。
        /// <para>实现类一旦可序列化，[SerializeReference] 场景下运行时状态将随域重载复活（快照污染）。</para>
        /// </summary>
        [Test]
        public void HandlerClasses_AreNotSerializable()
        {
            foreach (var (handler, impl, _) in s_Services)
            {
                Assert.IsFalse(handler.IsDefined(typeof(SerializableAttribute), false),
                    $"契约类 {handler.Name} 不应标注 [Serializable]（契约不参与序列化）");
                Assert.IsFalse(impl.IsDefined(typeof(SerializableAttribute), false),
                    $"实现类 {impl.Name} 不应标注 [Serializable]（运行时状态脱离序列化面是本次架构的核心目标）");
            }
        }

        /// <summary>
        /// 结构锁 b：实现类组合持有配置（仅约束带数据字段的 Config），且配置引用为 readonly。
        /// <para>无配置数据的后端允许不持有 m_Config；一旦持有必须 readonly 且类型可赋值对应 Config。</para>
        /// </summary>
        [Test]
        public void HandlerImpls_HoldConfigByComposition()
        {
            foreach (var (_, impl, configType) in s_Services)
            {
                FieldInfo field = impl.GetField("m_Config",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null)
                {
                    Assert.IsFalse(HasDataFields(configType),
                        $"配置类 {configType.Name} 含数据字段时实现类 {impl.Name} 必须组合持有 m_Config");
                    continue;
                }

                Assert.IsTrue(field.IsInitOnly, $"实现类 {impl.Name} 的 m_Config 应为 readonly");
                Assert.IsTrue(configType.IsAssignableFrom(field.FieldType),
                    $"实现类 {impl.Name} 的 m_Config 类型 {field.FieldType.Name} 应可赋值 {configType.Name}");
            }
        }

        /// <summary>
        /// 判断 Config 类型是否声明了实例字段（数据承载能力）。
        /// </summary>
        private static bool HasDataFields(Type configType)
        {
            return configType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0;
        }

        /// <summary>
        /// 结构锁 c：Config 类为纯数据工厂——含 CreateHandler 工厂、无生命周期/无行为成员。
        /// <para>同时锁定工厂返回类型为对应抽象 Handler 契约（配置与后端绑定关系）。</para>
        /// </summary>
        [Test]
        public void ConfigClasses_ArePureDataFactories()
        {
            foreach (var (handler, _, configType) in s_Services)
            {
                MethodInfo factory = configType.GetMethod("CreateHandler", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Assert.IsNotNull(factory, $"配置类 {configType.Name} 应声明 CreateHandler 工厂");
                Assert.AreEqual(handler, factory.ReturnType, $"配置类 {configType.Name}.CreateHandler 应返回 {handler.Name}");

                Assert.IsFalse(configType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Any(m => m.Name == "OnInit" || m.Name == "OnShutdown" || m.Name == "Initialize" || m.Name == "Tick"),
                    $"配置类 {configType.Name} 不得承载生命周期/行为成员（纯数据性）");
            }
        }

        /// <summary>
        /// 结构锁 d：Config 类与 Handler 契约/实现同命名空间配对，工厂实产出对应后端实例。
        /// </summary>
        [Test]
        public void Configs_BindToTheirHandlerContract()
        {
            foreach (var (handler, impl, configType) in s_Services)
            {
                Assert.AreEqual(handler.Namespace, configType.Namespace,
                    $"配置类 {configType.Name} 应与契约 {handler.Name} 同命名空间");
                Assert.AreEqual(impl.Namespace, configType.Namespace,
                    $"实现类 {impl.Name} 应与配置 {configType.Name} 同命名空间");

                object instance = Activator.CreateInstance(configType);
                object created = configType.GetMethod("CreateHandler").Invoke(instance, null);
                Assert.IsNotNull(created, $"配置类 {configType.Name}.CreateHandler 应产出实例");
                Assert.IsInstanceOf(handler, created, $"配置类 {configType.Name} 应产出 {handler.Name} 后端");
            }
        }

        /// <summary>
        /// 结构锁 e：Settings 持有 Config 字段（[SerializeReference] + [ProviderDropdown] 在位），
        /// 字段声明类型为抽象 Config——保证 Inspector 选择面是配置而非处理器实例。
        /// </summary>
        [Test]
        public void Settings_HoldConfigFields()
        {
            var settingsTypes = new (Type settings, string fieldName, Type configType)[]
            {
                (typeof(Res.ResourceServiceSettings), "m_HandlerConfig", typeof(Res.ResourceServiceHandlerConfig)),
                (typeof(Aud.AudioServiceSettings), "m_HandlerConfig", typeof(Aud.AudioServiceHandlerConfig)),
                (typeof(Cfg.ConfigTableServiceSettings), "m_HandlerConfig", typeof(Cfg.ConfigTableServiceHandlerConfig)),
                (typeof(Dbg.DebuggerServiceSettings), "m_HandlerConfig", typeof(Dbg.DebuggerServiceHandlerConfig)),
                (typeof(Inp.InputServiceSettings), "m_HandlerConfig", typeof(Inp.InputServiceHandlerConfig)),
                (typeof(Loc.LocalizationServiceSettings), "m_HandlerConfig", typeof(Loc.LocalizationServiceHandlerConfig)),
                (typeof(OPo.ObjectPoolServiceSettings), "m_HandlerConfig", typeof(OPo.ObjectPoolServiceHandlerConfig)),
                (typeof(OPo.GameObjectPoolServiceSettings), "m_HandlerConfig", typeof(OPo.GameObjectPoolServiceHandlerConfig)),
                (typeof(Sav.SaveServiceSettings), "m_HandlerConfig", typeof(Sav.SaveServiceHandlerConfig)),
                (typeof(Scn.SceneServiceSettings), "m_HandlerConfig", typeof(Scn.SceneServiceHandlerConfig)),
                (typeof(Tim.TimerServiceSettings), "m_HandlerConfig", typeof(Tim.TimerServiceHandlerConfig)),
                (typeof(UIm.UIServiceSettings), "m_HandlerConfig", typeof(UIm.UIServiceHandlerConfig)),
            };

            foreach (var (settings, fieldName, configType) in settingsTypes)
            {
                FieldInfo field = settings.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.IsNotNull(field, $"{settings.Name} 应声明字段 {fieldName}");
                Assert.AreEqual(configType, field.FieldType, $"{settings.Name}.{fieldName} 声明类型应为 {configType.Name}");
                Assert.IsTrue(field.IsDefined(typeof(SerializeReference), false),
                    $"{settings.Name}.{fieldName} 应标注 [SerializeReference]");
                Assert.IsTrue(field.IsDefined(typeof(Moirai.Atropos.ProviderDropdownAttribute), false),
                    $"{settings.Name}.{fieldName} 应标注 [ProviderDropdown]");
            }
        }
    }
}
