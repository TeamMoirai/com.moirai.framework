#if MEMORYPACK_INSTALLED
using MemoryPack;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// MemoryPack 存档后端测试：装了插件才有本夹具，验实现自述的线格式标识与字节往返。
    /// </summary>
    /// <remarks>
    /// 用例自 <c>Moirai.Atropos</c> 的 <c>SaveMigrationAndBackendTests</c> 拆出：容器级（真实文件 IO）通路要走框架 internal 管线， <br />
    /// 插件测试装配够不着（也不为它开 <c>InternalsVisibleTo</c>），故本夹具只按公开面验「注册表按自述标识有主 + 序列化器往返」。 <br />
    /// 本类必须 <c>partial</c>：MemoryPack 的 SourceGenerator 要求 <c>[MemoryPackable]</c> 类型的每一层容器都是 partial。
    /// </remarks>
    public partial class MemoryPackBackendTests
    {
        [MemoryPackable]
        internal sealed partial class MpkData
        {
            public int Hp;
            public string Name;
        }

        [Test]
        public void MemoryPackBackend_RoundTrips()
        {
            var serializer = new MemoryPackSaveSerializer();

            byte[] bytes = serializer.Serialize(new MpkData { Hp = 77, Name = "MemoryPack⑵" });
            MpkData loaded = serializer.Deserialize<MpkData>(bytes);

            Assert.AreEqual(77, loaded.Hp);
            Assert.AreEqual("MemoryPack⑵", loaded.Name);
        }

        [Test]
        public void MemoryPackBackend_RegisteredBySelfDescribedId()
        {
            var serializer = new MemoryPackSaveSerializer();

            // 断言的是插件装配自己的模块初始化器登记结果：按实现自述的 ID 查得到，且主人就是这支实现
            Assert.IsTrue(SaveSerializerRegistry.TryGet(serializer.BackendId, out ISaveSerializer registered),
                "模块初始化器应按实现自述标识登记后端，未装插件时本用例不存在");
            Assert.AreEqual(typeof(MemoryPackSaveSerializer), registered.GetType());
        }
    }
}
#endif
