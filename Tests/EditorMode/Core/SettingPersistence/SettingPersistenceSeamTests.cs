using System;
using System.Collections.Generic;
using System.IO;
using Core.SettingPersistence;
using UnityEditor.PackageManager;
using Moirai.Atropos;
using NUnit.Framework;

namespace Core.SettingPersistence
{
    /// <summary>落盘 checkpoint 的量具自检：计数后端本身得先可信，后面几刀的「写键不等于落盘」才判得准。</summary>
    /// <remarks>
    /// 政策是服务侧只写键、设置界面在用户确认时统一 <c>SettingUtility.Save()</c> 一次；
    /// 判据因此量的是「谁补了那一次 Save」，而写键路径必须一次都不算。
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

    /// <summary>图形侧的写入点只改配置项、一次都不落盘：一条行为格钉「批量移除后零 flush」，一条位置守卫防「写入入口里混进 flush」。</summary>
    /// <remarks>
    /// 落盘时机归设置界面：面板里预览过又放弃的改动不该已经写进档，所以四枚 <c>Set*Settings</c> 与 <c>RemoveSetting</c> 只写键。
    /// 四枚入口会经 <c>Screen</c> 与 <c>QualitySettings</c> 打真机状态，编辑器内不可靠，不为它们造行为格——
    /// 位置守卫只防「有人往写键的地方补一次 flush」，语义那枚牙在 <see cref="RemoveSettingClearsFourKeysWithoutFlushing"/> 上。 <br />
    /// 量具后端 <see cref="RecordingSettingHandler"/> 只编进 Tests.EditorMode，本文件因此不进 PlayMode。
    /// </remarks>
    [TestFixture]
    public sealed class GraphicsSettingsPersistenceTests
    {
        private static readonly string SourceRelativePath =
            Path.Combine("Runtime", "Core", "Graphics", "GraphicsSettings.SaveLoad.cs");

        [Test]
        public void RemoveSettingClearsFourKeysWithoutFlushing()
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

                Assert.AreEqual(0, recording.SaveCount, "清空也只改配置项：落盘由设置界面在确认时统一做");
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
        public void NoGraphicsWriteEntryFlushes()
        {
            string source = Path.Combine(ResolvePackageRoot(), SourceRelativePath);
            Assert.IsTrue(File.Exists(source), "取不到图形设置源文件，位置守卫会空转：" + source);

            string[] lines = File.ReadAllLines(source);
            List<string> offenders = new List<string>();
            int scanned = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string head = lines[i].TrimStart();
                // 注释里的 <see cref="SettingUtility.Save"/> 是指点调用方去哪落盘，不是这一行真落了盘
                if (head.StartsWith("//"))
                {
                    continue;
                }

                if (head.StartsWith("public static void Set") && head.Contains("Settings("))
                {
                    scanned++;
                }

                if (head.Contains("SettingUtility.Save("))
                {
                    offenders.Add((i + 1) + ": <" + head + ">");
                }
            }

            // 先钉扫描面：入口写法一改（换行、加修饰符）这守卫就会静默扫不到东西，那时「零违规」毫无含义
            Assert.AreEqual(4, scanned, "扫到的图形写入入口应是四枚，实际 " + scanned + " 枚：入口写法变了要同步改这条守卫");
            Assert.IsEmpty(offenders, "图形侧的写入入口一次都不落盘，flush 属于设置界面：\n" + string.Join("\n", offenders));
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
