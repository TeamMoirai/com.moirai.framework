using NUnit.Framework;
using UnityEngine.SceneManagement;
using Res = Moirai.Atropos.Resource;
using Scn = Moirai.Atropos.Scene;

namespace Service.Scene
{
    /// <summary>
    /// 场景登记簿回归测试：加载/卸载门禁、登记迁移、短名反向索引与关闭排空的纯决策矩阵。
    /// </summary>
    public sealed class SceneRegistryTests
    {
        #region 测试替身 [TEST DOUBLES]

        /// <summary>
        /// 最小场景句柄替身——仅承载身份与释放计数，不触碰引擎与资源后端。
        /// </summary>
        private sealed class FakeSceneHandle : Res.ResourceSceneHandle
        {
            internal int ReleaseCount;

            public override bool IsDone => true;
            public override float Progress => 1f;
            public override string Error => null;
            public override UnityEngine.SceneManagement.Scene SceneObject => default;
            public override bool UnSuspend() => true;
            public override bool ActivateScene() => true;
            public override Res.IResourceOperation UnloadAsync() => null;
            public override void Release() => ReleaseCount++;
        }

        private const string MAIN_LOCATION = "Assets/Scenes/main.unity";
        private const string MAIN_NAME = "main";
        private const string SUB_A = "Assets/Scenes/chunk_a.unity";
        private const string SUB_B = "Assets/Scenes/chunk_b.unity";
        private const string NAME_A = "chunk_a";

        private Scn.SceneRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new Scn.SceneRegistry();
        }

        /// <summary>
        /// 登记一个 Loaded 子场景（在途标记 → 前置登记 → 完成迁移的合法路径）。
        /// </summary>
        private Scn.SceneRegistry.SubSceneEntry RegisterLoadedSub(string location, string sceneName)
        {
            var handle = new FakeSceneHandle();
            _registry.TryMarkOperation(location);
            _registry.RegisterSubInFlight(location, handle);
            _registry.UnmarkOperation(location);
            _registry.CompleteSubLoad(location, sceneName);
            return new Scn.SceneRegistry.SubSceneEntry(handle, sceneName, Scn.SceneRegistry.ESubSceneState.Loaded);
        }

        #endregion

        #region 加载门禁 [LOAD GATING]

        [Test]
        public void CheckBeginLoad_FreshLocation_Allows()
        {
            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.Allow, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Additive));
            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.Allow, _registry.CheckBeginLoad(MAIN_LOCATION, LoadSceneMode.Single));
        }

        [Test]
        public void CheckBeginLoad_OperationInFlight_Rejects()
        {
            _registry.TryMarkOperation(SUB_A);

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.InFlight, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Additive));
            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.InFlight, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Single));
        }

        [Test]
        public void CheckBeginLoad_SubAlreadyRegistered_RejectsAdditive()
        {
            _registry.RegisterSubInFlight(SUB_A, new FakeSceneHandle());

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.SubAlreadyRegistered, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Additive));
        }

        [Test]
        public void CheckBeginLoad_MainInFlight_RejectsSingle()
        {
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.MainLoadInFlight, _registry.CheckBeginLoad("Assets/Scenes/other.unity", LoadSceneMode.Single));
        }

        [Test]
        public void CheckBeginLoad_RegisteredAsSub_RejectsSingle()
        {
            RegisterLoadedSub(SUB_A, NAME_A);

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.RegisteredAsSub, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Single));
        }

        [Test]
        public void CheckBeginLoad_RegisteredAsMain_RejectsAdditive()
        {
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());
            _registry.CompleteMainLoad(MAIN_LOCATION, MAIN_NAME, new FakeSceneHandle());

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.RegisteredAsMain, _registry.CheckBeginLoad(MAIN_LOCATION, LoadSceneMode.Additive));
        }

        [Test]
        public void CheckBeginLoad_InFlightMainLocation_RejectsAdditive()
        {
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());

            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.RegisteredAsMain, _registry.CheckBeginLoad(MAIN_LOCATION, LoadSceneMode.Additive));
        }

        #endregion

        #region 登记迁移 [REGISTRATION TRANSITIONS]

        [Test]
        public void CompleteSubLoad_TransitionsToLoaded_AndResolvesByName()
        {
            var handle = new FakeSceneHandle();
            _registry.RegisterSubInFlight(SUB_A, handle);

            var overwritten = _registry.CompleteSubLoad(SUB_A, NAME_A);

            Assert.IsNull(overwritten);
            Assert.IsTrue(_registry.TryGetSubScene(SUB_A, out var entry));
            Assert.AreEqual(Scn.SceneRegistry.ESubSceneState.Loaded, entry.State);
            Assert.AreSame(handle, entry.Handle);
            Assert.AreEqual(NAME_A, entry.SceneName);
            Assert.AreEqual(SUB_A, _registry.ResolveSubSceneLocation(NAME_A));
        }

        [Test]
        public void CompleteSubLoad_NameCollision_NewWinsAndReturnsPrevious()
        {
            RegisterLoadedSub(SUB_A, NAME_A);
            _registry.RegisterSubInFlight(SUB_B, new FakeSceneHandle());

            var overwritten = _registry.CompleteSubLoad(SUB_B, NAME_A);

            Assert.AreEqual(SUB_A, overwritten);
            Assert.AreEqual(SUB_B, _registry.ResolveSubSceneLocation(NAME_A));
        }

        [Test]
        public void CompleteMainLoad_SetsFieldsAndReturnsPreviousHandle()
        {
            var first = new FakeSceneHandle();
            _registry.RegisterMainInFlight(MAIN_LOCATION, first);
            var previous = _registry.CompleteMainLoad(MAIN_LOCATION, MAIN_NAME, first);
            Assert.IsNull(previous);
            Assert.AreEqual(MAIN_NAME, _registry.CurrentMainSceneName);
            Assert.AreEqual(MAIN_LOCATION, _registry.CurrentMainSceneLocation);

            var second = new FakeSceneHandle();
            const string nextLocation = "Assets/Scenes/level2.unity";
            _registry.RegisterMainInFlight(nextLocation, second);
            previous = _registry.CompleteMainLoad(nextLocation, "level2", second);

            Assert.AreSame(first, previous);
            Assert.AreEqual("level2", _registry.CurrentMainSceneName);
            Assert.IsNull(_registry.MainLoadingHandle);
        }

        [Test]
        public void AbandonLoad_Single_OnlyClearsMatchingInFlight()
        {
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());

            // 过期地址放弃不得误清当前在途
            _registry.AbandonLoad("Assets/Scenes/stale.unity", LoadSceneMode.Single);
            Assert.IsNotNull(_registry.MainLoadingHandle);

            _registry.AbandonLoad(MAIN_LOCATION, LoadSceneMode.Single);
            Assert.IsNull(_registry.MainLoadingHandle);
        }

        [Test]
        public void AbandonLoad_Additive_RemovesSubRegistration()
        {
            _registry.RegisterSubInFlight(SUB_A, new FakeSceneHandle());

            _registry.AbandonLoad(SUB_A, LoadSceneMode.Additive);

            Assert.IsFalse(_registry.TryGetSubScene(SUB_A, out _));
            Assert.AreEqual(Scn.SceneRegistry.ELoadGate.Allow, _registry.CheckBeginLoad(SUB_A, LoadSceneMode.Additive));
        }

        #endregion

        #region 卸载门禁与完成 [UNLOAD GATING & COMPLETION]

        [Test]
        public void TryBeginUnload_NotRegistered_Rejects()
        {
            Assert.AreEqual(Scn.SceneRegistry.EUnloadGate.NotRegistered, _registry.TryBeginUnload(SUB_A, out _));
            Assert.AreEqual(Scn.SceneRegistry.EUnloadGate.NotRegistered, _registry.TryBeginUnload(NAME_A, out _));
        }

        [Test]
        public void TryBeginUnload_StillLoading_Rejects()
        {
            _registry.RegisterSubInFlight(SUB_A, new FakeSceneHandle());

            Assert.AreEqual(Scn.SceneRegistry.EUnloadGate.StillLoading, _registry.TryBeginUnload(SUB_A, out _));
        }

        [Test]
        public void TryBeginUnload_OperationInFlight_Rejects()
        {
            var entry = RegisterLoadedSub(SUB_A, NAME_A);
            _registry.TryMarkOperation(SUB_A);

            Assert.AreEqual(Scn.SceneRegistry.EUnloadGate.InFlight, _registry.TryBeginUnload(SUB_A, out _));
        }

        [Test]
        public void TryBeginUnload_ByShortName_ResolvesAndMarks()
        {
            var expected = RegisterLoadedSub(SUB_A, NAME_A);

            var gate = _registry.TryBeginUnload(NAME_A, out var pending);

            Assert.AreEqual(Scn.SceneRegistry.EUnloadGate.Allow, gate);
            Assert.AreEqual(SUB_A, pending.Location);
            Assert.AreEqual(NAME_A, pending.SceneName);
            Assert.AreSame(expected.Handle, pending.Handle);
            Assert.IsTrue(_registry.IsOperationMarked(SUB_A));
        }

        [Test]
        public void CompleteUnload_RemovesIndexOnlyWhenMappingMatches()
        {
            // 同名碰撞：索引被后者覆盖后，前者卸载不得移除索引
            RegisterLoadedSub(SUB_A, NAME_A);
            RegisterLoadedSub(SUB_B, NAME_A);

            _registry.TryBeginUnload(SUB_A, out _);
            Assert.IsTrue(_registry.CompleteUnload(SUB_A, NAME_A));
            Assert.AreEqual(SUB_B, _registry.ResolveSubSceneLocation(NAME_A));

            _registry.TryBeginUnload(SUB_B, out _);
            Assert.IsTrue(_registry.CompleteUnload(SUB_B, NAME_A));
            Assert.IsNull(_registry.ResolveSubSceneLocation(NAME_A));
        }

        #endregion

        #region 查询与关闭 [QUERY & SHUTDOWN]

        [Test]
        public void IsContainScene_CoversMainSubAndInFlight()
        {
            _registry.CaptureActiveMainScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            RegisterLoadedSub(SUB_A, NAME_A);
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());

            Assert.IsTrue(_registry.IsContainScene(MAIN_LOCATION));
            Assert.IsTrue(_registry.IsContainScene(SUB_A));
            Assert.IsTrue(_registry.IsContainScene(NAME_A));
            Assert.IsFalse(_registry.IsContainScene("Assets/Scenes/unknown.unity"));
            Assert.IsFalse(_registry.IsContainScene(null));
        }

        [Test]
        public void IsMainScene_MatchesNameAndLocation()
        {
            _registry.RegisterMainInFlight(MAIN_LOCATION, new FakeSceneHandle());
            _registry.CompleteMainLoad(MAIN_LOCATION, MAIN_NAME, new FakeSceneHandle());

            Assert.IsTrue(_registry.IsMainScene(MAIN_NAME));
            Assert.IsTrue(_registry.IsMainScene(MAIN_LOCATION));
            Assert.IsFalse(_registry.IsMainScene(NAME_A));
            Assert.IsFalse(_registry.IsMainScene(null));
        }

        [Test]
        public void SnapshotLoadedSubScenes_ExcludesLoading()
        {
            RegisterLoadedSub(SUB_A, NAME_A);
            _registry.RegisterSubInFlight(SUB_B, new FakeSceneHandle());

            var snapshot = _registry.SnapshotLoadedSubScenes();

            Assert.AreEqual(1, snapshot.Count);
            Assert.Contains(SUB_A, snapshot);
        }

        [Test]
        public void Shutdown_DrainsEveryHandleOnceAndResets()
        {
            var loadedSub = new FakeSceneHandle();
            _registry.RegisterSubInFlight(SUB_A, loadedSub);
            _registry.CompleteSubLoad(SUB_A, NAME_A);

            var loadingSub = new FakeSceneHandle();
            _registry.RegisterSubInFlight(SUB_B, loadingSub);

            // 主场景已完成 + 另一主场景在途（CompleteMainLoad 会清在途，故在途登记须后于完成）
            var main = new FakeSceneHandle();
            _registry.RegisterMainInFlight(MAIN_LOCATION, main);
            _registry.CompleteMainLoad(MAIN_LOCATION, MAIN_NAME, main);

            var mainLoading = new FakeSceneHandle();
            _registry.RegisterMainInFlight("Assets/Scenes/next.unity", mainLoading);

            var drained = _registry.Shutdown(out var mainLoadingHandle, out var mainHandle);

            Assert.AreSame(mainLoading, mainLoadingHandle);
            Assert.AreSame(main, mainHandle);
            Assert.AreEqual(2, drained.Count);
            Assert.IsFalse(_registry.IsContainScene(SUB_A));
            Assert.IsFalse(_registry.IsContainScene(MAIN_LOCATION));
            Assert.IsNull(_registry.MainSceneHandle);
            Assert.IsNull(_registry.MainLoadingHandle);
            Assert.AreEqual(0, _registry.SnapshotLoadedSubScenes().Count);
        }

        #endregion
    }
}
