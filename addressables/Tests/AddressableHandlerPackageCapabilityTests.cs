using System;
using System.Reflection;
using NUnit.Framework;

namespace Moirai.Plugin.Addressables.Tests
{
    /// <summary>
    /// addressables 插件的后端能力用例：包名轴能力必须由后端自报为假，编辑器侧的包名下拉行只认这条自述。
    /// </summary>
    /// <remarks>
    /// 这条判据原先写死在框架 Editor 装配的 <c>switch (handler)</c> 里（按具体后端类型判）；后端拆进插件装配后框架认不到它， <br />
    /// 判据只能由被判定方自报。框架处理器基类上的默认值为真，本后端覆写为假——
    /// 缺了这枚覆写，包名行会被按内建后端的收集器清单错误接管（画出一个后端根本不看重的下拉）。 <br />
    /// 后端类型是插件装配里的 internal，故按全名在当前域反射定位，取不到时整组忽略。
    /// </remarks>
    public sealed class AddressableHandlerPackageCapabilityTests
    {
        private const string AddressableHandlerTypeName = "Moirai.Atropos.Resource.AddressableHandler";
        private const string BuiltinHandlerTypeName = "Moirai.Atropos.Resource.YooAssetHandler";
        private const string CapabilityPropertyName = "ConsumesPackageNames";

        /// <summary>
        /// Addressables 后端必须自报「不消费包名轴」——编辑器包名行的接管判据全靠这条自述。
        /// </summary>
        [Test]
        public void AddressableHandler_ReportsPackageNamesNotConsumed()
        {
            object handler = CreateInstance(AddressableHandlerTypeName);
            if (handler == null)
            {
                Assert.Ignore(
                    "AddressableHandler is not compiled (addressables plugin absent or ADDRESSABLES_INSTALLED undefined).");
                return;
            }

            Assert.AreEqual(false, ReadConsumesPackageNames(handler),
                "Addressables 只有一张隐式目录、没有包名轴：它必须自报不消费包名，否则编辑器会按收集器清单画包名下拉行");
        }

        /// <summary>
        /// 默认档必须仍是「消费包名轴」：内建后端与没有编辑器桥的自定义后端都按包名轴取清单，覆写只该由不消费包名的后端自己发起。
        /// </summary>
        [Test]
        public void BuiltinHandler_KeepsDefaultPackageNamesConsumed()
        {
            object handler = CreateInstance(BuiltinHandlerTypeName);
            if (handler == null)
            {
                Assert.Ignore("Built-in resource handler is absent in current domain.");
                return;
            }

            Assert.AreEqual(true, ReadConsumesPackageNames(handler),
                "默认档必须是真：自定义后端与内建后端都按包名轴取清单，只有不消费包名的后端自己覆写");
        }

        /// <summary>
        /// 读后端自报的包名轴能力（接缝成员是 <c>protected internal</c>，跨装配只能经反射取）。
        /// </summary>
        private static bool ReadConsumesPackageNames(object handler)
        {
            PropertyInfo capability = handler.GetType().GetProperty(CapabilityPropertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(capability, "{0} not found on handler.", CapabilityPropertyName);
            return (bool)capability.GetValue(handler, null);
        }

        private static object CreateInstance(string handlerTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(handlerTypeName, false);
                if (type != null)
                {
                    return Activator.CreateInstance(type);
                }
            }

            return null;
        }
    }
}
