using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Policy
{
    /// <summary>
    /// 设置读写落点守卫：设置键名的拼装只允许出现在对应模块的政策文件里，后端不碰 <c>SettingUtility</c>。
    /// </summary>
    /// <remarks>
    /// 两条判据各管一个失效面：键名规则按<strong>键名空间</strong>判——轨级音量键的第二份拼装藏在 <c>Models/AudioGroupConfig.cs</c> 这类不叫 <c>*Handler.cs</c> 的文件里，
    /// 按文件名判的规则看不见它；持久化规则按后端文件名判，管的是「谁在决定何时落盘」。 <br />
    /// 判据扫磁盘源文件（经 <see cref="PackageInfo"/> 定位包根，不依赖当前工作目录），改名或搬目录不会让它静默少查一个文件；
    /// 第三格是量具自检：政策文件取不到、或它里面已经不含该键名时判红，否则前两条的「零命中」毫无含义。
    /// 匹配前先剥掉行注释——「不再经由旧 X」这类说明文字不是拼装站点，留着会让守卫误报一次就花光门禁信任。
    /// </remarks>
    [TestFixture]
    public sealed class SettingsPolicyLocationGuardTests
    {
        // 设置模块自己的后端文件：它们本来就该碰 SettingUtility
        private static readonly string[] SettingsBackendFiles =
        {
            "SettingHandler.cs", "SettingUtility.cs", "DefaultSettingHandler.cs",
            "DefaultSettingHandler.Serializer.cs", "DefaultSettingHandler.Settings.cs", "PlayerPrefsSettingHandler.cs"
        };

        // 键名空间规则：一条键只允许在一个政策文件里被拼出来
        private static readonly (string Key, string PolicyFile)[] KeyRules =
        {
            ("GameConstant.Setting.AUDIO_", "AudioSettingsPolicy.cs"),
            ("GameConstant.Setting.LANGUAGE", "LocalizationSettingsPolicy.cs")
        };

        [Test]
        public void SettingKeys_AreComposedOnlyInsideTheirPolicyFile()
        {
            List<string> offenders = new List<string>();

            foreach (string file in Directory.GetFiles(ResolveRuntimeRoot(), "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                string text = StripLineComments(File.ReadAllText(file));

                foreach ((string key, string policyFile) in KeyRules)
                {
                    if (name != policyFile && text.Contains(key, StringComparison.Ordinal))
                    {
                        offenders.Add(name + " 拼了 " + key + "，应只在 " + policyFile + " 里");
                    }
                }
            }

            Assert.IsEmpty(offenders, "设置键名的拼装只允许在对应模块的政策文件里：\n" + string.Join("\n", offenders));
        }

        [Test]
        public void BackendHandlerFiles_DoNotTouchSettingUtility()
        {
            string[] offenders = Directory.GetFiles(ResolveRuntimeRoot(), "*Handler.cs", SearchOption.AllDirectories)
                .Where(f => !SettingsBackendFiles.Contains(Path.GetFileName(f)))
                .Where(f => StripLineComments(File.ReadAllText(f)).Contains("SettingUtility.", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.IsEmpty(offenders,
                "后端不碰持久化：读写请落在政策文件（AudioSettingsPolicy / LocalizationSettingsPolicy）或装配侧：\n"
                + string.Join("\n", offenders));
        }

        [Test]
        public void Scanner_FindsThePolicyFilesItGuards()
        {
            string runtimeRoot = ResolveRuntimeRoot();

            foreach ((string key, string policyFile) in KeyRules)
            {
                string found = Directory.GetFiles(runtimeRoot, policyFile, SearchOption.AllDirectories).FirstOrDefault();
                Assert.IsNotNull(found, policyFile + " 不在 Runtime 下：前两条守卫会因为扫不到文件而空转");

                StringAssert.Contains(key, StripLineComments(File.ReadAllText(found)),
                    policyFile + " 里已经没有 " + key + "：键名搬走后要连同本守卫的规则一起改");
            }
        }

        /// <summary>剥掉行注释（含文档注释），只留代码行参与匹配。</summary>
        private static string StripLineComments(string text)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int index = lines[i].IndexOf("//", StringComparison.Ordinal);
                if (index >= 0)
                {
                    lines[i] = lines[i].Substring(0, index);
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>解析被测包的 <c>Runtime/</c> 根目录（与 <see cref="ReflectionPolicyGuardTests"/> 取测试根同一手法）。</summary>
        private static string ResolveRuntimeRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(Moirai.Atropos.GameApp).Assembly);
            Assert.IsNotNull(package, "无法定位 Moirai.Atropos 所属包，落点守卫无法解析源码根目录。");

            string runtimeRoot = Path.Combine(package.resolvedPath, "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), "源码目录不存在：" + runtimeRoot);

            return runtimeRoot;
        }
    }
}
