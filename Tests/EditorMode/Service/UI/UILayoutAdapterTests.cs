using System.Text;
using Moirai.Atropos.UI.Adapter;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.UI
{
    /// <summary>
    /// 布局适配器的同值写入跳过：重复适配保持布局不变、外部挪动仍被写回。
    /// </summary>
    /// <remarks>
    /// 「跳过写入」本身落在 uGUI 内部脏标记上，编辑器侧无法直接观测，故此处锁的是它的两个可见后果： <br />
    /// 内容未变时适配前后布局逐字段一致、外部改动仍被纠正到目标位；跳过判据的等价性由 <br />
    /// <c>SetInsetAndSizeFromParentEdge</c> / <c>SetSizeWithCurrentAnchors</c> 的后置条件保证。
    /// </remarks>
    [TestFixture]
    public sealed class UILayoutAdapterTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject(nameof(UILayoutAdapterTests));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UObject.DestroyImmediate(_root);
                _root = null;
            }
        }

        private RectTransform NewRect(string name, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            return rect;
        }

        [Test]
        public void VerticalAdapter_RepeatedAdapt_KeepsLayout_AndCorrectsExternalMove()
        {
            var parent = NewRect("parent", new Vector2(500f, 400f));
            var adapter = parent.gameObject.AddComponent<VerticalAdapter>();
            adapter.m_Gap = 10f;

            var first = NewRect("first", new Vector2(100f, 30f));
            first.SetParent(parent, false);
            var second = NewRect("second", new Vector2(100f, 50f));
            second.SetParent(parent, false);
            var third = NewRect("third", new Vector2(100f, 20f));
            third.SetParent(parent, false);

            adapter.Adapt();

            // 顶端贴边的 inset 依次为 0 / 前一项高 + 间隙 的累加；父级高度为总高（含间隙）
            Assert.AreEqual(0f, -first.offsetMax.y, 0.001f, "首个元素的上边距为 0");
            Assert.AreEqual(40f, -second.offsetMax.y, 0.001f, "第二个元素的上边距 = 前一项高 + 间隙");
            Assert.AreEqual(100f, -third.offsetMax.y, 0.001f, "第三个元素的上边距 = 前两项高 + 两个间隙");
            Assert.AreEqual(30f, first.sizeDelta.y, 0.001f, "子项高度不被适配改写");
            Assert.AreEqual(120f, parent.sizeDelta.y, 0.001f, "父级高度 = 总高 + 两个间隙");

            var before = Snapshot(first, second, third, parent);
            adapter.Adapt();
            Assert.AreEqual(before, Snapshot(first, second, third, parent), "内容未变时重复适配必须逐字段不变");

            // 外部挪动第二个元素：下一次适配仍要把它写回目标位（跳过判据不得吃掉纠正）
            second.anchoredPosition += new Vector2(0f, 15f);
            adapter.Adapt();
            Assert.AreEqual(before, Snapshot(first, second, third, parent), "外部挪动后必须写回，且与首次适配的布局一致");
        }

        [Test]
        public void AngleAdapter_RepeatedAdapt_KeepsPose_AndCorrectsExternalRotation()
        {
            var parent = NewRect("parent", new Vector2(300f, 300f));
            var adapter = parent.gameObject.AddComponent<AngleAdapter>();
            adapter.m_Distance = 100f;
            adapter.m_Gap = 30f;
            adapter.m_Clockwise = true;

            var items = new RectTransform[3];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = NewRect($"item{i}", new Vector2(40f, 40f));
                items[i].SetParent(parent, false);
            }

            adapter.Adapt();

            var before = Snapshot(items);
            adapter.Adapt();
            Assert.AreEqual(before, Snapshot(items), "内容未变时重复适配必须逐字段不变");

            // 外部转动第二项：下一次适配写回其槽位姿态
            items[1].localRotation = Quaternion.identity;
            adapter.Adapt();
            Assert.AreEqual(before, Snapshot(items), "外部转动后必须写回目标姿态");
            Assert.AreNotEqual(Quaternion.identity, items[1].localRotation, "第二项应回到槽位角度而非停留在单位旋转");
        }

        private static string Snapshot(params RectTransform[] rects)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < rects.Length; i++)
            {
                var rect = rects[i];
                builder.Append(rect.offsetMin.ToString("R")).Append('|')
                    .Append(rect.offsetMax.ToString("R")).Append('|')
                    .Append(rect.sizeDelta.ToString("R")).Append('|')
                    .Append(rect.anchoredPosition.ToString("R")).Append('|')
                    .Append(rect.localPosition.ToString("R")).Append('|')
                    .Append(rect.localRotation.ToString("R")).Append('\n');
            }

            return builder.ToString();
        }
    }
}
