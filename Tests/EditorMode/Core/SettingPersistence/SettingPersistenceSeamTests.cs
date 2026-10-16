using System;
using System.Collections.Generic;
using System.IO;
using Core.SettingPersistence;
using UnityEditor.PackageManager;
using Moirai.Atropos;
using NUnit.Framework;

namespace Core.SettingPersistence
{
    /// <summary>落盘 checkpoint 的量具自检：计数后端本身得先可信，后面三刀才谈得上"谁补了那一次 Save"。</summary>
    /// <remarks>
    /// 真缺陷是 <c>SettingUtility.Save()</c> 全仓只有调试器调过：文件后端下每个 <c>Set*</c> 只写内存字典，重启即丢。
    /// 政策要在服务的确认入口补一次 flush，判据就是这个计数——所以先钉住「写键不等于落盘、显式 Save 才 +1」。
    /// </remarks>
    [TestFixture]
    public sealed class SettingPersistenceSeamTests
    {
        private SettingHandler _previous;
        private RecordingSettingHandler _recording;

        [SetUp]
        public void SetUp()
        {
            _previous = SettingUtility.Internal_PeekHandler();
            _recording = new RecordingSettingHandler();
            SettingUtility.Internal_UseHandler(_recording);
        }

        [TearDown]
        public void TearDown()
        {
            SettingUtility.Internal_UseHandler(_previous);
            _recording = null;
        }

        [Test]
        public void RecordingBackendCountsSavesAndKeepsWrittenValues()
        {
            SettingUtility.SetString(GameConstant.Setting.LANGUAGE, "zh-hans");

            Assert.AreEqual(0, _recording.SaveCount, "Set* 本身不该算 flush：政策要判的正是有没有人补那一次 Save");
            Assert.IsTrue(SettingUtility.Save(), "量具后端的 Save 恒真，判据因此只反映调用次数");
            Assert.AreEqual(1, _recording.SaveCount);

            Assert.IsTrue(_recording.TryGetStored(GameConstant.Setting.LANGUAGE, out var stored), "写过的键要取回得到");
            Assert.AreEqual("zh-hans", stored);
        }

        [Test]
        public void RecordingBackend_DoesNotPersistAcrossSwaps()
        {
            // 反对照：计数后端只在注册期间有效，换回生产后端后不得把内存值当存档读出来
            SettingUtility.SetString(GameConstant.Setting.LANGUAGE, "zh-hans");
            SettingUtility.Internal_UseHandler(_previous);
            try
            {
                Assert.IsFalse(ReferenceEquals(_previous, _recording), "前提：换回去的确实是原本那份后端");
                Assert.AreEqual(0, _recording.SaveCount, "换出后没人再叫它 Save，计数不得自己长");
            }
            finally
            {
                SettingUtility.Internal_UseHandler(_recording);
            }
        }
    }
}


// 图形侧的落盘判据放在本文件里，是因为这台编辑器对 Tests/EditorMode/Core/Graphics 目录的导入登记是坏的：
// 删掉目录与文件 meta、改 asmdef 逼它重算源清单、连发两轮 recompile 之后，新文件的类名仍不在装配里，
// 而 Unity 又只在无改动时不重写清单——从外部无法强制全量 reimport。命名空间保持 Core.Graphics，用例全名不变。
namespace Core.Graphics
{

    /// <summary>图形侧的确认入口要落盘：一条行为格钉住「批量移除后恰一次 flush」，一条位置守卫防「四枚写入入口漏掉 flush」。</summary>
    /// <remarks>
    /// 四枚 <c>Set*Settings</c> 会经 <c>Screen</c> 与 <c>QualitySettings</c> 打真机状态，编辑器内不可靠，不为它们造行为格——
    /// 位置守卫只防漏，不证明语义；语义那枚牙在 <see cref="RemoveSettingClearsFourKeysAndFlushesOnce"/> 上。
    /// 量具后端 <see cref="RecordingSettingHandler"/> 只编进 Tests.EditorMode，本文件因此不进 PlayMode。 <br />
    /// 位置守卫只防「新加一个写入入口忘了补 flush」，不证明语义；语义由 <see cref="RemoveSettingClearsFourKeysAndFlushesOnce"/> 钉。
    /// </remarks>
    [TestFixture]
    public sealed class GraphicsSettingsPersistenceTests
    {
        private static readonly string SourceRelativePath =
            Path.Combine("Runtime", "Core", "Graphics", "GraphicsSettings.SaveLoad.cs");

        [Test]
        public void RemoveSettingClearsFourKeysAndFlushesOnce()
        {
            var previous = SettingUtility.Internal_PeekHandler();
            var recording = new RecordingSettingHandler();
            SettingUtility.Internal_UseHandler(recording);
            try
            {
                SettingUtility.SetString(GameConstant.Setting.GRAPHICS_VSYNC, "1");
                SettingUtility.SetInt(GameConstant.Setting.GRAPHICS_RESOLUTION, 3);
                Assert.AreEqual(0, recording.SaveCount, "写键本身不该算 flush");

                GraphicsSettings.RemoveSetting();

                Assert.AreEqual(1, recording.SaveCount, "批量移除后恰好吃一次 flush，不是每条键一次");
                Assert.IsFalse(recording.TryGetStored(GameConstant.Setting.GRAPHICS_VSYNC, out _));
                Assert.IsFalse(recording.TryGetStored(GameConstant.Setting.GRAPHICS_RESOLUTION, out _),
                    "四条键都要摘干净，否则下次启动仍按旧分辨率改");
            }
            finally
            {
                SettingUtility.Internal_UseHandler(previous);
            }
        }

        [Test]
        public void EveryGraphicsCheckpointEndsWithASingleFlush()
        {
            string source = Path.Combine(ResolvePackageRoot(), SourceRelativePath);
            Assert.IsTrue(File.Exists(source), "取不到图形设置源文件，位置守卫会空转：" + source);

            string[] lines = File.ReadAllLines(source);
            List<string> offenders = new List<string>();
            int scanned = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string head = lines[i].TrimStart();
                if (!head.StartsWith("public static void Set") || !head.Contains("Settings("))
                {
                    continue;
                }

                scanned++;
                int brace = 0;
                string last = string.Empty;
                for (int j = i; j < lines.Length; j++)
                {
                    string body = lines[j].Trim();
                    int before = brace;
                    foreach (char c in body)
                    {
                        if (c == '{') { brace++; }
                        else if (c == '}') { brace--; }
                    }

                    if (before > 0 && brace == 0)
                    {
                        // 这一行闭合了方法：闭合花括号不算方法体的最后一句
                        break;
                    }

                    if (body.Length > 0 && brace > 0 && !body.StartsWith("//") && !body.StartsWith("///"))
                    {
                        last = body;
                    }
                }

                if (last != "SettingUtility.Save();")
                {
                    offenders.Add((i + 1) + ": 方法体末尾是 <" + last + ">");
                }
            }

            // 先钉扫描面：入口写法一改（换行、加修饰符）这守卫就会静默扫不到东西，那时「零违规」毫无含义
            Assert.AreEqual(4, scanned, "扫到的图形确认入口应是四枚，实际 " + scanned + " 枚：入口写法变了要同步改这条守卫");
            Assert.IsEmpty(offenders, "四枚确认入口每一枚都要以一次 flush 收尾：\n" + string.Join("\n", offenders));
        }

        private static string ResolvePackageRoot()
        {
            // 本文件的命名空间里 GameApp 会先绑到同名命名空间（CS0118）；换成本测试已经在用的框架类型取程序集
            PackageInfo package = PackageInfo.FindForAssembly(typeof(SettingUtility).Assembly);
            Assert.IsNotNull(package, "无法定位 Moirai.Atropos 所属包，图形落盘守卫无法解析源文件路径。");
            return package.resolvedPath;
        }
    }

}
