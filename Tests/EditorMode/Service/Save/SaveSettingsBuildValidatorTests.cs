using System;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// <see cref="SaveSettingsBuildValidator"/> 开严判据的单元测试：显式覆写优先于构建档默认。
    /// </summary>
    /// <remarks>
    /// 构建档默认（非 Development 开严）需要真实 BuildReport，单测环境无法构造——本套件只钉
    /// 显式覆写通道的优先级语义（1 开 / 0 关），并钉「未设置环境变量时 report 必填」的契约。 <br />
    /// 构建档真值由出包验证承担（CI 构建时环境变量未设置即按构建档生效）。 <br />
    /// 环境变量测试间串扰按快照/还原处理（TearDown 恒还原）。
    /// </remarks>
    public class SaveSettingsBuildValidatorTests
    {
        private string _original;

        [SetUp]
        public void SetUp()
        {
            _original = Environment.GetEnvironmentVariable(SaveSettingsBuildValidator.STRICT_ENVIRONMENT_VARIABLE);
            Environment.SetEnvironmentVariable(SaveSettingsBuildValidator.STRICT_ENVIRONMENT_VARIABLE, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(SaveSettingsBuildValidator.STRICT_ENVIRONMENT_VARIABLE, _original);
        }

        [Test]
        public void IsStrictMode_ExplicitOne_True()
        {
            Environment.SetEnvironmentVariable(SaveSettingsBuildValidator.STRICT_ENVIRONMENT_VARIABLE, "1");
            Assert.IsTrue(SaveSettingsBuildValidator.IsStrictMode(null),
                "显式 1 强制开严（含开发构建），不触构建档");
        }

        [Test]
        public void IsStrictMode_ExplicitZero_False()
        {
            Environment.SetEnvironmentVariable(SaveSettingsBuildValidator.STRICT_ENVIRONMENT_VARIABLE, "0");
            Assert.IsFalse(SaveSettingsBuildValidator.IsStrictMode(null),
                "显式 0 强制关严（发布构建也放行，责任自负），不触构建档");
        }

        [Test]
        public void IsStrictMode_UnsetNullReport_Throws()
        {
            // 未设置环境变量时判据取自构建档——report 必填（真实调用点 OnPreprocessBuild 恒持 report），
            // 以 ArgumentNullException 显式表达契约，不靠 NRE
            Assert.Throws<ArgumentNullException>(() => SaveSettingsBuildValidator.IsStrictMode(null));
        }
    }
}
