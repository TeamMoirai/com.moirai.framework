using System.Collections.Generic;
using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 插件依赖闭包、拓扑序与冲突判定的用例集。
    /// </summary>
    internal sealed class InstallPlannerTests
    {
        private static readonly PluginVersion CurrentUnity = new PluginVersion(2022, 3, 0);
        private static readonly PluginVersion CurrentFramework = new PluginVersion(1, 1, 0);
        private static readonly string[] NoIds = new string[0];

        private static PluginManifest Plug(string id, string version = "1.0.0", PluginDependency[] deps = null,
            PluginUpmDependency[] upm = null, string floor = "2022.3.0", string frameworkFloor = "1.0.0")
        {
            return new PluginManifest
            {
                id = id,
                name = id,
                function = "Tools",
                version = version,
                minUnityVersion = floor,
                minFrameworkVersion = frameworkFloor,
                dependencies = deps,
                upmDependencies = upm
            };
        }

        private static PluginDependency Dep(string id, string version, string op)
        {
            return new PluginDependency { id = id, version = version, versionOperator = op };
        }

        private static PluginUpmDependency Upm(string id, string version)
        {
            return new PluginUpmDependency { id = id, version = version };
        }

        /// <summary>
        /// a→b→c 三层链、d 自环、e 缺依赖、f 要求高于目录候选、g 地板高于当前编辑器。
        /// </summary>
        private static List<PluginManifest> Catalog()
        {
            return new List<PluginManifest>
            {
                Plug("a", "1.0.0", new[] { Dep("b", "1.0.0", ">=") }),
                Plug("b", "1.0.0", new[] { Dep("c", "1.0.0", ">=") }, new[] { Upm("com.x.y", "1.2.0") }),
                Plug("c", "1.0.0", null, new[] { Upm("com.x.y", "1.0.0") }),
                Plug("d", "1.0.0", new[] { Dep("d", "1.0.0", ">=") }),
                Plug("e", "1.0.0", new[] { Dep("missing", "1.0.0", ">=") }),
                Plug("f", "1.0.0", new[] { Dep("b", "2.0.0", ">") }),
                Plug("g", "1.0.0", null, null, "9999.0.0"),
                Plug("h", "1.0.0", null, null, "2022.3.0", "9999.0.0")
            };
        }

        private static string IdsOf(InstallPlan plan)
        {
            var names = new List<string>();
            foreach (PluginManifest manifest in plan.ToInstall) names.Add(manifest.id);
            return string.Join(",", names);
        }

        private static PlanConflict OnlyConflict(InstallPlan plan)
        {
            Assert.AreEqual(1, plan.Conflicts.Count, string.Join(" | ", ConflictReasons(plan)));
            return plan.Conflicts[0];
        }

        private static List<string> ConflictReasons(InstallPlan plan)
        {
            var reasons = new List<string>();
            foreach (PlanConflict conflict in plan.Conflicts) reasons.Add(conflict.Reason);
            return reasons;
        }

        [Test]
        public void Plan_Chain_DepsComeOutFirst()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "a" }, NoIds, CurrentUnity);

            Assert.IsFalse(plan.HasConflicts, string.Join(" | ", ConflictReasons(plan)));
            Assert.AreEqual("c,b,a", IdsOf(plan));
        }

        [Test]
        public void Plan_SelfCycle_ReportsConflictAndAbortsBatch()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "d" }, NoIds, CurrentUnity);

            Assert.IsTrue(plan.HasConflicts);
            PlanConflict conflict = OnlyConflict(plan);
            Assert.AreEqual(PlanConflictKind.Cycle, conflict.Kind);
            Assert.AreEqual("d", conflict.PluginId);
            Assert.IsEmpty(plan.ToInstall, "有冲突必须整批中止，不能留下一半可装清单");
        }

        [Test]
        public void Plan_MissingDependency_ReportsNamedReason()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "e" }, NoIds, CurrentUnity);

            PlanConflict conflict = OnlyConflict(plan);
            Assert.AreEqual(PlanConflictKind.MissingDependency, conflict.Kind);
            StringAssert.Contains("missing", conflict.Reason);
            Assert.AreEqual("e", conflict.PluginId);
        }

        [Test]
        public void Plan_VersionUnsatisfied_ReportsOperatorAndBothVersions()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "f" }, NoIds, CurrentUnity);

            PlanConflict conflict = OnlyConflict(plan);
            Assert.AreEqual(PlanConflictKind.VersionUnsatisfied, conflict.Kind);
            StringAssert.Contains(">", conflict.Reason);
            StringAssert.Contains("2.0.0", conflict.Reason);
            StringAssert.Contains("1.0.0", conflict.Reason);
        }

        [Test]
        public void Plan_UnityFloorAboveCurrent_ReportsFloorUnmet()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "g" }, NoIds, CurrentUnity);

            PlanConflict conflict = OnlyConflict(plan);
            Assert.AreEqual(PlanConflictKind.UnityFloorUnmet, conflict.Kind);
            Assert.AreEqual("g", conflict.PluginId);
            StringAssert.Contains("9999.0.0", conflict.Reason);
            Assert.IsEmpty(plan.ToInstall);
        }

        [Test]
        public void Plan_FrameworkFloorAboveCurrent_ReportsFloorUnmet()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "h" }, NoIds, CurrentUnity, CurrentFramework);

            PlanConflict conflict = OnlyConflict(plan);
            Assert.AreEqual(PlanConflictKind.FrameworkFloorUnmet, conflict.Kind);
            Assert.AreEqual("h", conflict.PluginId);
            StringAssert.Contains("9999.0.0", conflict.Reason);
            Assert.IsEmpty(plan.ToInstall);
        }

        [Test]
        public void Plan_FrameworkFloorNotProvided_SkipsTheCheck()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "h" }, NoIds, CurrentUnity);

            Assert.IsFalse(plan.HasConflicts, string.Join(" | ", ConflictReasons(plan)));
            Assert.AreEqual("h", IdsOf(plan));
        }

        [Test]
        public void Plan_AlreadyInstalledAndSatisfying_IsNotReinstalled()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), new[] { "a", "b", "c" }, new[] { "a" }, NoIds, CurrentUnity);

            Assert.IsFalse(plan.HasConflicts, string.Join(" | ", ConflictReasons(plan)));
            Assert.IsEmpty(plan.ToInstall);
        }

        [Test]
        public void Plan_ForceRequested_ReinstallsEvenIfPresent()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), new[] { "a", "b", "c" }, new[] { "a" }, new[] { "a" }, CurrentUnity);

            Assert.IsFalse(plan.HasConflicts, string.Join(" | ", ConflictReasons(plan)));
            Assert.AreEqual("a", IdsOf(plan), "强制只作用于点名的那一个");
        }

        [Test]
        public void Plan_SatisfiedDependencyTree_MergesUpmDepsTakingHighestVersion()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "a" }, NoIds, CurrentUnity);

            Assert.AreEqual(1, plan.MergedUpmDependencies.Count, string.Join(",", IdsOf(plan)));
            Assert.AreEqual("com.x.y", plan.MergedUpmDependencies[0].id);
            Assert.AreEqual("1.2.0", plan.MergedUpmDependencies[0].version);
        }

        [Test]
        public void Plan_SharedDependency_CheckedPerEdgeAcrossRoots()
        {
            InstallPlan plan = InstallPlanner.Plan(Catalog(), NoIds, new[] { "a", "f" }, NoIds, CurrentUnity);

            Assert.IsTrue(plan.HasConflicts, "共享依赖的版本约束按边各判一次，f 那条仍要红");
            Assert.AreEqual("f", OnlyConflict(plan).PluginId);
            Assert.IsEmpty(plan.ToInstall);
        }

        [Test]
        public void DependentsOf_ReturnsInstalledDependentsOnly()
        {
            IReadOnlyList<string> dependents = InstallPlanner.DependentsOf(Catalog(), "b", new[] { "a", "c" });

            Assert.AreEqual(new[] { "a" }, dependents);
        }

        [Test]
        public void DependentsOf_TransitiveDependent_IsIncluded()
        {
            IReadOnlyList<string> dependents = InstallPlanner.DependentsOf(Catalog(), "c", new[] { "a", "b" });

            Assert.AreEqual(new[] { "b", "a" }, dependents, "卸载 c 前要知道整条在场的依赖方");
        }
    }
}
