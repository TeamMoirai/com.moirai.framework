using System.Collections.Generic;
using Dbg = Moirai.Atropos.Debugger;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Debugger
{
    /// <summary>
    /// 调试器窗口注册表测试：路径树构建、检索、选中、剪除与版本递增。
    /// </summary>
    public sealed class DebuggerWindowRegistryTests
    {
        #region 测试桩 [TEST FAKES]

        private sealed class StubWindow : Dbg.IDebuggerWindow
        {
            public void Initialize(params object[] args)
            {
            }

            public void Shutdown()
            {
            }

            public void OnEnter()
            {
            }

            public void OnLeave()
            {
            }

            public void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
            }

            public VisualElement CreateView()
            {
                return new VisualElement();
            }
        }

        #endregion

        #region 注册与检索 [REGISTRATION AND LOOKUP]

        [Test]
        public void Register_FlatPath_CreatesRootLeaf()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var window = new StubWindow();

            registry.Register("Console", window);

            Assert.AreEqual(1, registry.WindowCount);
            Assert.AreSame(window, registry.GetWindow("Console"));
            Assert.AreEqual(1, registry.Root.Children.Count, "根下应只有一个顶层节点");
            Assert.AreEqual("Console", registry.Root.Children[0].Name);
        }

        [Test]
        public void Register_NestedPath_CreatesIntermediateGroups()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var window = new StubWindow();

            registry.Register("Profiler/Memory/Texture", window);

            Assert.AreSame(window, registry.GetWindow("Profiler/Memory/Texture"));
            Assert.IsNull(registry.GetWindow("Profiler"), "中间目录节点不是窗口");
            Assert.AreEqual(1, registry.Root.Children.Count, "根下 Profiler 目录");

            Dbg.DebuggerWindowNode profiler = registry.Root.Children[0];
            Assert.IsTrue(profiler.IsGroup, "Profiler 应为目录节点");
            Assert.AreEqual(1, profiler.Children.Count, "Profiler 下 Memory 目录");

            Dbg.DebuggerWindowNode memory = profiler.Children[0];
            Assert.IsTrue(memory.IsGroup, "Memory 应为目录节点");
            Assert.AreSame(window, memory.Children[0].Window, "Texture 叶子应绑定窗口");
            Assert.AreEqual("Memory", memory.Name);
            Assert.AreEqual("Profiler/Memory", memory.Path, "中间节点 Path 应为完整目录路径");
        }

        [Test]
        public void Register_DuplicatePath_Throws()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            registry.Register("Console", new StubWindow());

            Assert.Throws<GameException>(() => registry.Register("Console", new StubWindow()),
                "重复注册同一路径应抛出 GameException");
        }

        [Test]
        public void Register_PathOccupiedByWindow_Throws()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            registry.Register("Console", new StubWindow());

            Assert.Throws<GameException>(() => registry.Register("Console/Sub", new StubWindow()),
                "窗口节点上不允许再挂子路径（目录与窗口互斥）");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("A//B")]
        [TestCase("A/")]
        [TestCase("/A")]
        public void Register_InvalidPath_Throws(string path)
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();

            Assert.Throws<GameException>(() => registry.Register(path, new StubWindow()),
                "无效路径应抛出 GameException: " + path);
        }

        [Test]
        public void Register_NullWindow_Throws()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();

            Assert.Throws<GameException>(() => registry.Register("Console", null),
                "空窗口应抛出 GameException");
        }

        [Test]
        public void GetWindow_UnknownPath_ReturnsNull()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();

            Assert.IsNull(registry.GetWindow("Not/Registered"));
            Assert.IsNull(registry.GetWindow(null));
        }

        #endregion

        #region 注销与剪除 [UNREGISTER AND PRUNING]

        [Test]
        public void Unregister_Leaf_PrunesEmptyAncestors()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var window = new StubWindow();
            registry.Register("A/B/C", window);

            Assert.IsTrue(registry.Unregister("A/B/C"));
            Assert.IsNull(registry.GetWindow("A/B/C"));
            Assert.AreEqual(0, registry.Root.Children.Count, "空的祖先目录链应被整体剪除");
        }

        [Test]
        public void Unregister_KeepsSharedAncestors()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            registry.Register("A/B/C", new StubWindow());
            registry.Register("A/B/D", new StubWindow());

            Assert.IsTrue(registry.Unregister("A/B/C"));

            Assert.IsNull(registry.GetWindow("A/B/C"));
            Assert.IsNotNull(registry.GetWindow("A/B/D"), "共享目录下其余窗口不应受影响");
            Assert.AreEqual(1, registry.Root.Children.Count, "A 目录仍应保留");
        }

        [Test]
        public void Unregister_UnknownPath_ReturnsFalse()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();

            Assert.IsFalse(registry.Unregister("Not/Registered"));
        }

        [Test]
        public void Unregister_SelectedWindow_ClearsSelection()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var window = new StubWindow();
            registry.Register("Console", window);
            registry.SelectWindow("Console");

            Assert.IsTrue(registry.Unregister("Console"));
            Assert.IsNull(registry.SelectedWindow, "注销选中窗口后应清空选中");
        }

        #endregion

        #region 选中 [SELECTION]

        [Test]
        public void SelectWindow_ResolvesAndExpandsAncestors()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var window = new StubWindow();
            registry.Register("A/B/C", window);
            Dbg.DebuggerWindowNode groupA = registry.Root.Children[0];

            Assert.IsTrue(registry.SelectWindow("A/B/C"));
            Assert.AreSame(window, registry.SelectedWindow);
            Assert.AreSame(window, registry.SelectedNode.Window);
            Assert.IsTrue(groupA.Expanded, "选中深层窗口应展开祖先目录");
        }

        [Test]
        public void SelectWindow_GroupPath_ReturnsFalse()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            registry.Register("A/B", new StubWindow());

            Assert.IsFalse(registry.SelectWindow("A"), "目录节点不可选中");
        }

        [Test]
        public void SelectWindow_UnknownPath_ReturnsFalse()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();

            Assert.IsFalse(registry.SelectWindow("Unknown"));
        }

        #endregion

        #region 版本 [VERSION]

        [Test]
        public void Version_IncrementsOnStructuralChange()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            int initial = registry.Version;

            registry.Register("A", new StubWindow());
            int afterRegister = registry.Version;
            Assert.Greater(afterRegister, initial, "注册应递增版本");

            registry.SelectWindow("A");
            Assert.Greater(registry.Version, afterRegister, "选中应递增版本");

            registry.Unregister("A");
            Assert.Greater(registry.Version, afterRegister + 1, "注销应递增版本");
        }

        [Test]
        public void CollectWindows_ReturnsAllRegistered()
        {
            Dbg.DebuggerWindowRegistry registry = new Dbg.DebuggerWindowRegistry();
            var windowA = new StubWindow();
            var windowB = new StubWindow();
            registry.Register("A/One", windowA);
            registry.Register("B/Two", windowB);

            List<Dbg.IDebuggerWindow> results = new List<Dbg.IDebuggerWindow>();
            registry.CollectWindows(results);

            Assert.AreEqual(2, results.Count);
            Assert.Contains(windowA, results);
            Assert.Contains(windowB, results);
        }

        #endregion
    }
}
