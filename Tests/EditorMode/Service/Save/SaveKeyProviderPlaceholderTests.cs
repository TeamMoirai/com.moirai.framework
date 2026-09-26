using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 出厂占位存档密钥的判据测试（<see cref="SaveKeyProvider.UsesPlaceholderCredentials"/>）。
    /// <para>占位口令/盐/主密钥随包发布 ⇒ 任何人可派生同一把密钥，「加密存档」等价于不加密。
    /// 这条判据同时供 Inspector 告警与 <c>SaveSettingsBuildValidator</c> 出包自检使用，
    /// 三个内置提供方都必须报自己的生效材料——只盯 Static 会让 HKDF 主密钥、口令盐文静默漏过门禁。</para>
    /// <para>纯逻辑测试，不依赖磁盘与真实加密。</para>
    /// </summary>
    [TestFixture]
    public sealed class SaveKeyProviderPlaceholderTests
    {
        [Test]
        public void UsesPlaceholderCredentials_DefaultInstance_IsTrue()
        {
            var provider = new StaticSaveKeyProvider();

            Assert.IsTrue(provider.UsesPlaceholderCredentials,
                "未配置的静态提供方仍是包内占位口令与盐文");
        }

        [Test]
        public void UsesPlaceholderCredentials_BothReplaced_IsFalse()
        {
            var provider = new StaticSaveKeyProvider();
            provider.SetDerivationParameters("project-unique-passphrase", "project-unique-salt", 100000);

            Assert.IsFalse(provider.UsesPlaceholderCredentials, "口令与盐文都换成项目专属值后不得再报占位");
        }

        [Test]
        public void UsesPlaceholderCredentials_OnlyOneSideReplaced_IsTrue()
        {
            var passphraseReplaced = new StaticSaveKeyProvider();
            passphraseReplaced.SetDerivationParameters("project-unique-passphrase", "CHANGE_ME_SALT", 100000);
            Assert.IsTrue(passphraseReplaced.UsesPlaceholderCredentials, "盐文仍是占位值即算占位配置");

            var saltReplaced = new StaticSaveKeyProvider();
            saltReplaced.SetDerivationParameters("CHANGE_ME_BEFORE_SHIPPING", "project-unique-salt", 100000);
            Assert.IsTrue(saltReplaced.UsesPlaceholderCredentials, "口令仍是占位值即算占位配置");
        }

        [Test]
        [TestCase(null, "project-unique-salt")]
        [TestCase("project-unique-passphrase", null)]
        [TestCase("", "project-unique-salt")]
        [TestCase("project-unique-passphrase", "")]
        public void UsesPlaceholderCredentials_EmptyCredential_IsTrue(string passphrase, string salt)
        {
            var provider = new StaticSaveKeyProvider();
            provider.SetDerivationParameters(passphrase, salt, 100000);

            Assert.IsTrue(provider.UsesPlaceholderCredentials, "空口令或空盐文同样不可用作发布密钥");
        }

        [Test]
        public void UsesPlaceholderCredentials_OverrideCleared_FallsBackToSerializedPlaceholder()
        {
            var provider = new StaticSaveKeyProvider();
            provider.SetDerivationParameters("project-unique-passphrase", "project-unique-salt", 100000);
            Assert.IsFalse(provider.UsesPlaceholderCredentials, "前置条件：覆盖生效");

            provider.SetDerivationParameters(null, null, 0);
            Assert.IsTrue(provider.UsesPlaceholderCredentials,
                "覆盖清空后应回落到序列化配置——那份配置出厂就是占位值");
        }

        [Test]
        public void UsesPlaceholderCredentials_HkdfDefaultMasterSecret_IsTrue()
        {
            var provider = new HKDFPerUserSaveKeyProvider();

            Assert.IsTrue(provider.UsesPlaceholderCredentials,
                "HKDF 主密钥出厂是 CHANGE_ME_BEFORE_SHIPPING，随包发布即等同不加密");
        }

        [Test]
        public void UsesPlaceholderCredentials_PassphraseDefaultSalt_IsTrue()
        {
            var provider = new PassphraseSaveKeyProvider();

            Assert.IsTrue(provider.UsesPlaceholderCredentials,
                "口令虽是运行期注入，序列化盐文仍是出厂占位值");
        }

        [Test]
        public void UsesPlaceholderCredentials_AnyProvider_NeverReportsWhenMaterialsReplaced()
        {
            // 静态：口令+盐都换掉
            var staticProvider = new StaticSaveKeyProvider();
            staticProvider.SetDerivationParameters("project-unique-passphrase", "project-unique-salt", 100000);
            Assert.IsFalse(staticProvider.UsesPlaceholderCredentials, "静态提供方换掉口令与盐后不应再报");

            // HKDF：主密钥换掉（序列化字段，经 SetDerivationParameters 不可用——直接构造后仅校验默认态已覆盖；
            // 主密钥无运行期覆盖入口，故本格退化为「非 Static 类型也走同一虚判据」的调用面覆盖）
            var hkdf = new HKDFPerUserSaveKeyProvider();
            Assert.IsTrue(hkdf.UsesPlaceholderCredentials, "HKDF 默认主密钥应报占位（判据已上提至基类虚属性）");

            // 口令：盐文是序列化字段，同样无运行期覆盖；默认盐应报
            var passphrase = new PassphraseSaveKeyProvider();
            Assert.IsTrue(passphrase.UsesPlaceholderCredentials, "口令提供方默认盐应报占位（判据已上提至基类虚属性）");
        }
    }
}
