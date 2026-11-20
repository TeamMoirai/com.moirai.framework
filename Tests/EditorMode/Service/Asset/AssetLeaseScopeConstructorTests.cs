using Moirai.Atropos;
using Moirai.Atropos.Asset;
using NUnit.Framework;

namespace Service.Asset
{
    /// <summary>
    /// AssetLeaseScope 双构造路径的契约守卫：注入构造与无参构造均保持惰性语义。
    /// </summary>
    /// <remarks>
    /// 机制类不调静态门面：注入构造直接持有 <see cref="IAssetService"/>，无参构造首次 <c>Load</c> 才经容器解析； <br />
    /// 未加载即 Dispose 在两条构造路径下都不得触碰资产契约——注入 null 即可让「误触达」以 NRE 形式暴露。
    /// </remarks>
    [TestFixture]
    public class AssetLeaseScopeConstructorTests
    {
        [Test]
        public void Dispose_BeforeLoad_BothConstructorsStayLazy()
        {
            // 无参构造：未 Load 即 Dispose——不触发容器解析（此域未注册任何服务，解析即抛）
            var lazy = new AssetLeaseScope();
            Assert.DoesNotThrow(lazy.Dispose, "无参构造未加载即 Dispose 不应触发容器解析");
            Assert.IsTrue(lazy.IsDisposed);
            Assert.AreEqual(0, lazy.LeaseCount);

            // 注入构造：注入 null 即可验证 Dispose 不触碰契约实例（触碰即 NRE）
            var injected = new AssetLeaseScope(null);
            Assert.DoesNotThrow(injected.Dispose, "注入构造未加载即 Dispose 不应触碰资产契约");
            Assert.IsTrue(injected.IsDisposed);
            Assert.AreEqual(0, injected.LeaseCount);
        }

        [Test]
        public void Dispose_SecondCall_IdempotentBothConstructors()
        {
            var lazy = new AssetLeaseScope();
            lazy.Dispose();
            Assert.DoesNotThrow(lazy.Dispose, "二次 Dispose 应幂等");

            var injected = new AssetLeaseScope(null);
            injected.Dispose();
            Assert.DoesNotThrow(injected.Dispose, "二次 Dispose 应幂等");
        }
    }
}
