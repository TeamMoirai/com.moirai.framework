using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 出厂占位存档密钥的判据测试（<see cref="StaticSaveKeyProvider.UsesPlaceholderCredentials"/>）。
    /// <para>占位口令/盐随包发布 ⇒ 任何人可派生同一把密钥，「加密存档」等价于不加密。
    /// 这条判据同时供 Inspector 告警与 <c>SaveSettingsBuildValidator</c> 出包自检使用，
    /// 所以它必须只看生效值（运行期覆盖优先于序列化配置），且不依赖磁盘与真实加密。</para>
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
    }
}
