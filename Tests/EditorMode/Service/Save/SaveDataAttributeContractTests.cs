using System;
using System.Linq;
using System.Reflection;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// SaveDataAttribute.Backend 的元数据形状契约守卫：钉住它是 ushort，且具名参数按 ushort 装箱。
    /// </summary>
    /// <remarks>
    /// 反射用途属白名单第 1 类（断成员标注做契约守卫）：装箱形态只能经 <see cref="CustomAttributeData"/> 观察，不能靠编译期判据。 <br />
    /// 之所以用镜像特性而不是 <see cref="SaveDataAttribute"/>：真实标注的类型会进 TypeCache 扫描面，被菜单驱动的快照导出收录进项目产物。 <br />
    /// 判据针对的是装箱形态本身——存档模式快照导出器按 <c>is ushort</c> 取具名参数，取不到时静默按 JSON 跳过该类型，编译与用例都不会报。
    /// </remarks>
    public class SaveDataAttributeContractTests
    {
        [AttributeUsage(AttributeTargets.Class)]
        private sealed class MirrorBackendAttribute : Attribute
        {
            public ushort Backend { get; set; }
        }

        [MirrorBackend(Backend = SaveBackendIds.MESSAGE_PACK)]
        private sealed class MirrorSample
        {
        }

        [Test]
        public void BackendMember_IsUshort_AttributeNamedArgumentStillAcceptable()
        {
            PropertyInfo member = typeof(SaveDataAttribute).GetProperty(nameof(SaveDataAttribute.Backend));
            Assert.IsNotNull(member, "[SaveData] 的 Backend 具名参数须存在");
            Assert.AreEqual(typeof(ushort), member.PropertyType, "特性参数只能取编译期常量：ushort 常量表可写，struct 标识不可写");
        }

        [Test]
        public void UshortNamedArgument_BoxesAsUshort_NotInt_SnapshotExporterProbeHolds()
        {
            CustomAttributeNamedArgument named = CustomAttributeData
                .GetCustomAttributes(typeof(MirrorSample)).Single()
                .NamedArguments.Single(a => a.MemberName == nameof(MirrorBackendAttribute.Backend));

            Assert.AreEqual(typeof(ushort), named.TypedValue.ArgumentType);
            Assert.IsTrue(named.TypedValue.Value is ushort, "导出器按 is ushort 取具名参数");
            Assert.IsFalse(named.TypedValue.Value is int, "ushort 具名参数不按 int 装箱：按 int 取就静默判不出后端");
            Assert.AreEqual(SaveBackendIds.MESSAGE_PACK, (ushort)named.TypedValue.Value);
        }
    }
}
