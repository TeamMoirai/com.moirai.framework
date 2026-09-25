using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 编辑器预览取资产入口的门禁：<see cref="ResourceService.LoadAssetForEditor"/>。
    /// <para>预览与工具面统一从这里取资产，判据是「同一份地址、同一个对象」——它必须与
    /// <c>AssetDatabase.LoadAssetAtPath</c> 给出同一个实例，否则 Inspector 里看到的"对"就不是运行期那份。</para>
    /// <para>这里只钉取数口径，不验租约：编辑态这条路刻意不建记录、不返租约（每次重绘租一份就是纯泄漏）。</para>
    /// </summary>
    public sealed class EditorPreviewAssetTests
    {
        [Test]
        public void EmptyLocation_ReturnsNull()
        {
            Assert.IsNull(ResourceService.LoadAssetForEditor(null));
            Assert.IsNull(ResourceService.LoadAssetForEditor(string.Empty));
        }

        [Test]
        public void UnknownLocation_ReturnsNullInsteadOfThrowing()
        {
            // 预览在 Inspector 的重绘路径上，取不到只能是 null——抛出去会让组件面板打不开
            Assert.IsNull(ResourceService.LoadAssetForEditor("Assets/__MoiraiPreviewProbe__/Absent.png"));
        }

        [Test]
        public void KnownAssetPath_ReturnsTheSameObjectAsAssetDatabase()
        {
            string path = FindAnyImportedAssetPath();
            if (path == null)
            {
                Assert.Ignore("工程里找不到可直读的贴图资产，正向取数用例无从验证。");
                return;
            }

            var expected = AssetDatabase.LoadAssetAtPath<Object>(path);
            Assert.IsNotNull(expected, $"资产库本身取不到 {path}，是夹具挑错了路径");

            var actual = ResourceService.LoadAssetForEditor(path);
            Assert.AreSame(expected, actual, "预览入口与 AssetDatabase 直读不是同一个对象：地址到资产的换算分叉了");
        }

        /// <summary>取一份工程里必然导入过的贴图路径（没有则返回 null，由用例自行 Ignore）。</summary>
        private static string FindAnyImportedAssetPath()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            return guids.Length == 0 ? null : AssetDatabase.GUIDToAssetPath(guids[0]);
        }
    }
}
