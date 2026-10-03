using System;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 声明式后端自注册用例：<c>[RegisterSerializer]</c> 实现经 SaveServiceCodegen 生成器写进模块初始化器，项目侧不写引导代码即已在注册表占号。
    /// </summary>
    /// <remarks>
    /// 本文件不含任何 <c>SaveSerializerRegistry.Register</c> 调用——判据完全由生成器产物决定，摘掉标注即判红。 <br />
    /// 实现须是程序集内可访问的类型（注册代码发在独立的初始化器类里，私有嵌套类型访问不到，形状不符落 MIRAI309）。 <br />
    /// 不带 <c>[Serializable]</c>：避免被 ProviderDropdown 的类型扫描收进生产资产的候选。
    /// </remarks>
    public class SaveSerializerSelfRegistrationTests
    {
        /// <summary>测试专用后端标识（框架保留区之外，且避开 1000/1100 这两格既有夹具）。</summary>
        private const ushort SelfRegisteredBackend = 1300;

        /// <summary>
        /// 被生成器登记的序列化后端：无参构造 + 编译期常量 <c>BackendId</c>（判重依赖后者）。
        /// </summary>
        [RegisterSerializer]
        internal sealed class SelfRegisteredSerializer : ISaveSerializer
        {
            public ushort BackendId => SelfRegisteredBackend;

            public byte[] Serialize<T>(T data)
            {
                return Array.Empty<byte>();
            }

            public T Deserialize<T>(byte[] bytes)
            {
                return default;
            }
        }

        [Test]
        public void AnnotatedSerializer_IsRegisteredWithoutBootstrapCode()
        {
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SelfRegisteredBackend, out ISaveSerializer serializer),
                "标注 [RegisterSerializer] 的实现应由模块初始化器登记，本夹具没有任何 Register 调用");
            Assert.IsInstanceOf<SelfRegisteredSerializer>(serializer, "该 ID 的读写主应是标注的那份实现");
        }

        [Test]
        public void AnnotatedSerializer_LeavesBuiltInBackendsServingTheirOwnIds()
        {
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SelfRegisteredBackend, out ISaveSerializer custom));
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out ISaveSerializer json));

            Assert.IsInstanceOf<SelfRegisteredSerializer>(custom);
            Assert.IsInstanceOf<JsonSaveSerializer>(json, "自注册不得顶掉内置后端的标识");
        }
    }
}
