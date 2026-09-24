using System;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace Utility
{
    /// <summary>
    /// 验证几何随机点从 UnityEngine.Random 换到框架随机源后分布仍然正确——
    /// 圆内/球内的"均匀"靠的是半径取 sqrt(u) / cbrt(u)，漏掉这一步点会往圆心堆。
    /// </summary>
    public class MathsUtilityRandomTests
    {
        private ulong _seedBefore;

        [SetUp]
        public void SetUp()
        {
            _seedBefore = RandomUtility.Seed;
            RandomUtility.Reseed(1717UL);
        }

        [TearDown]
        public void TearDown()
        {
            RandomUtility.Reseed(_seedBefore);
        }

        [Test]
        public void RandomVector2_And_3_StayInsideTheBox()
        {
            var min = new Vector2(-3f, 10f);
            var max = new Vector2(5f, 12f);
            for (int i = 0; i < 2000; i++)
            {
                var v = MathsUtility.RandomVector2(min, max);
                Assert.GreaterOrEqual(v.x, -3f);
                Assert.LessOrEqual(v.x, 5f);
                Assert.GreaterOrEqual(v.y, 10f);
                Assert.LessOrEqual(v.y, 12f);
            }

            var min3 = new Vector3(0f, -1f, 2f);
            var max3 = new Vector3(1f, 0f, 4f);
            for (int i = 0; i < 2000; i++)
            {
                var v = MathsUtility.RandomVector3(min3, max3);
                Assert.GreaterOrEqual(v.z, 2f);
                Assert.LessOrEqual(v.z, 4f);
            }
        }

        [Test]
        public void RandomPointOnCircle_And_Sphere_HaveExactlyRequestedRadius()
        {
            for (int i = 0; i < 500; i++)
            {
                Assert.AreEqual(2.5f, MathsUtility.RandomPointOnCircle(2.5f).magnitude, 1e-3f);
                Assert.AreEqual(3f, MathsUtility.RandomPointOnSphere(3f).magnitude, 1e-3f);
            }
        }

        [Test]
        public void RandomPointInsideUnitCircle_IsAreaUniform()
        {
            // 面积均匀的圆盘 E|p| = 2/3；半径直接取 u（少一步 sqrt）会得到 0.5，往圆心堆
            double sum = 0;
            const int n = 60000;
            for (int i = 0; i < n; i++) sum += MathsUtility.RandomPointInsideUnitCircle().magnitude;

            Assert.AreEqual(2.0 / 3.0, sum / n, 0.01, "圆内取点的半径分布");
        }

        [Test]
        public void RandomPointInsideUnitSphere_IsVolumeUniform()
        {
            // 体积均匀的球 E|p| = 3/4
            double sum = 0;
            const int n = 60000;
            float maxMagnitude = 0f;
            for (int i = 0; i < n; i++)
            {
                var p = MathsUtility.RandomPointInsideUnitSphere();
                sum += p.magnitude;
                maxMagnitude = Mathf.Max(maxMagnitude, p.magnitude);
            }

            Assert.LessOrEqual(maxMagnitude, 1.0001f, "不得跑出单位球");
            Assert.AreEqual(0.75, sum / n, 0.01, "球内取点的半径分布");
        }

        [Test]
        public void RandomPointOnUnitSphere_IsSurfaceUniform()
        {
            // 球面均匀时 z 分量的均值应为 0、方差应为 1/3
            double sum = 0, sum2 = 0;
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                float z = MathsUtility.RandomPointOnUnitSphere().z;
                Assert.GreaterOrEqual(1f, Mathf.Abs(z), "z 不得越出 [-1,1]");
                sum += z;
                sum2 += z * z;
            }

            Assert.AreEqual(0.0, sum / n, 0.01);
            Assert.AreEqual(1.0 / 3.0, sum2 / n - Math.Pow(sum / n, 2), 0.01);
        }

        [Test]
        public void RollADice_StaysOnTheFaceRange()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 4000; i++)
            {
                int v = MathsUtility.RollADice(6);
                Assert.GreaterOrEqual(v, 1);
                Assert.LessOrEqual(v, 6);
                seen.Add(v);
            }

            Assert.AreEqual(6, seen.Count);
            Assert.AreEqual(1, MathsUtility.RollADice(1), "单面骰回到 1");
        }

        [Test]
        public void Chance_EndpointsAreExact()
        {
            // 旧写法 Range(0,100) <= percent 让 0% 也有 1% 的成功率
            for (int i = 0; i < 20000; i++)
            {
                Assert.IsFalse(MathsUtility.Chance(0), "0% 不该有任何成功");
                Assert.IsTrue(MathsUtility.Chance(100), "100% 不该有任何失败");
            }
        }

        [Test]
        public void Chance_MiddleIsRoughlyRight()
        {
            int hits = 0;
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                if (MathsUtility.Chance(30)) hits++;
            }

            Assert.AreEqual(0.30, (double)hits / n, 0.01);
        }

        [Test]
        public void RandomNumber_CoversRangeInsteadOfRepeatingOneValue()
        {
            // 旧实现每次 new System.Random()，同一 tick 内的调用会拿到同一个种子同一个值
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 500; i++) seen.Add(MathsUtility.RandomNumber(0, 1000));

            // 500 抽自 1000 个值，期望不同值约 393（旧实现会塌缩到 1 个）
            Assert.Greater(seen.Count, 300);
        }

        [Test]
        public void UnityUtility_PointHelpers_StayInsideTheirShape()
        {
            for (int i = 0; i < 3000; i++)
            {
                var p2 = UnityUtility.GetRandomPointInCircle(Vector2.one * 4f, 2f);
                Assert.LessOrEqual((p2 - Vector2.one * 4f).magnitude, 2.0001f);

                var p3 = UnityUtility.GetRandomPointInSphere(Vector3.one * 2f, 1.5f);
                Assert.LessOrEqual((p3 - Vector3.one * 2f).magnitude, 1.5001f);

                var ring = UnityUtility.GetRandomPointInCircle(Vector2.zero, 1f, 3f);
                Assert.LessOrEqual(ring.magnitude, 3.0001f);
            }
        }

        [Test]
        public void ColorRandoms_StayInRange()
        {
            var min = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            var max = new Color(0.3f, 0.5f, 0.7f, 0.9f);
            for (int i = 0; i < 500; i++)
            {
                var c = Color.white.RandomColor(min, max);
                Assert.GreaterOrEqual(c.r, 0.2f);
                Assert.LessOrEqual(c.r, 0.3001f);
                Assert.GreaterOrEqual(c.a, 0.8f);
                Assert.LessOrEqual(c.a, 0.9001f);
            }

            var fromPalette = ColorsUtility.RandomColor();
            Assert.GreaterOrEqual(fromPalette.r, 0f);
            Assert.LessOrEqual(Mathf.Max(fromPalette.r, Mathf.Max(fromPalette.g, fromPalette.b)), 1f);
        }
    }
}
