using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 编辑器预览取资产的门禁：<see cref="ResourceService.TryLoadAsset{T}"/> 在服务未初始化时的编辑态分支。
    /// </summary>
    /// <remarks>
    /// 判据是「同一份地址、同一个对象」——必须与 <c>AssetDatabase.LoadAssetAtPath</c> 给出同一实例，
    /// 否则 Inspector 里看到的"对"就不是运行期那份。 <br />
    /// 编辑态分支不取租约、不进记录表（Inspector 每次重绘都会调用，每次重绘租一份就是纯泄漏）；
    /// 同一次调用在运行期已初始化时改走租约路径，所以预览侧的调用点都先判非播放态。
    /// </remarks>
    public sealed class EditorPreviewAssetTests
    {
        [Test]
        public void EmptyLocation_ReturnsNull()
        {
            Assert.IsFalse(ResourceService.TryLoadAsset<Object>(null, out var emptyAsset));
            Assert.IsNull(emptyAsset);

            Assert.IsFalse(ResourceService.TryLoadAsset<Object>(string.Empty, out emptyAsset));
            Assert.IsNull(emptyAsset);
        }

        [Test]
        public void UnknownLocation_ReturnsNullInsteadOfThrowing()
        {
            // 预览在 Inspector 的重绘路径上，取不到只能是 false——抛出去会让组件面板打不开
            Assert.IsFalse(ResourceService.TryLoadAsset<Object>(
                "Assets/__MoiraiPreviewProbe__/Absent.png", out var missingAsset));
            Assert.IsNull(missingAsset);
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

            Assert.IsTrue(ResourceService.TryLoadAsset<Object>(path, out var actual));
            Assert.AreSame(expected, actual, "取用族的编辑态分支与 AssetDatabase 直读不是同一个对象：地址到资产的换算分叉了");
        }

        /// <summary>
        /// 取一份工程里必然导入过的贴图路径（没有则返回 null，由用例自行 Ignore）。
        /// </summary>
        private static string FindAnyImportedAssetPath()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D");
            return guids.Length == 0 ? null : AssetDatabase.GUIDToAssetPath(guids[0]);
        }
    }
}
