using System.Diagnostics;
using Moirai.Atropos;
using Moirai.Atropos.ObjectPool;
using NUnit.Framework;
using Debug = UnityEngine.Debug;
using Time = UnityEngine.Time;

namespace ObjectPoolTests
{
    /// <summary>
    /// 通用对象池性能基准（[Explicit] 手动运行，不进常规测试流程）。
    /// <para>编辑器 Mono 基准噪声约 ±2x，数据仅作回归趋势参考，不作绝对性能结论；
    /// 需要结论时以同一工具、同一数据做 before/after 对照实测。</para>
    /// </summary>
    [Explicit]
    public sealed class GenericObjectPoolBenchmark
    {
        #region 常量 [CONSTANTS]

        private const int WARMUP_ROUNDS = 100_000;
        private const int MEASURE_ROUNDS = 1_000_000;
        private const int POOL_SIZE = 1024;

        #endregion

        #region 测试桩 [TEST FAKE]

        private sealed class BenchObject : ObjectBase
        {
            /// <summary>
            /// 暴露 Initialize 供基准填充目标。
            /// </summary>
            /// <param name="target">引用目标。</param>
            public void Init(object target)
            {
                Initialize(target);
            }

            protected internal override void Release(bool isShutdown)
            {
            }
        }

        #endregion

        #region 基础设施 [INFRASTRUCTURE]

        private DefaultObjectPoolHandler _handler;
        private IObjectPool<BenchObject> _pool;
        private BenchObject[] _registered;

        [SetUp]
        public void SetUp()
        {
            _handler = new DefaultObjectPoolHandler();
            _handler.Internal_Init();
            _pool = _handler.GetOrCreatePool<BenchObject>(new ObjectPoolCreateOptions(capacity: POOL_SIZE * 2));
            _registered = new BenchObject[POOL_SIZE];
            for (int i = 0; i < POOL_SIZE; i++)
            {
                BenchObject obj = MemoryPool.Acquire<BenchObject>();
                obj.Init(new object());
                _pool.Register(obj, false);
                _registered[i] = obj;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
            _registered = null;
            _pool = null;
            _handler = null;
        }

        #endregion

        #region 基准 [BENCHMARKS]

        /// <summary>
        /// Spawn/Despawn 往返——池热路径主指标。
        /// </summary>
        [Test]
        public void Spawn_Despawn_Roundtrip()
        {
            RunWarmup();

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < MEASURE_ROUNDS; i++)
            {
                BenchObject obj = _pool.Spawn();
                _pool.Despawn(obj);
            }
            sw.Stop();

            Debug.Log(StringUtility.Format(
                "[GenericObjectPoolBenchmark] Spawn/Despawn roundtrip x{0}: {1:F2} ms ({2:F1} ns/op)",
                MEASURE_ROUNDS, sw.Elapsed.TotalMilliseconds, sw.Elapsed.TotalMilliseconds * 1_000_000.0 / MEASURE_ROUNDS));
        }

        /// <summary>
        /// 过期清扫——维护路径吞吐（每唤醒最多 8 个，驱动至清空）。
        /// </summary>
        [Test]
        public void Maintenance_ExpiredSweep()
        {
            IObjectPool<BenchObject> expirePool = _handler.GetOrCreatePool<BenchObject>(
                new ObjectPoolCreateOptions("Expire", expireTime: 10f));
            for (int i = 0; i < POOL_SIZE; i++)
            {
                BenchObject obj = MemoryPool.Acquire<BenchObject>();
                obj.Init(new object());
                expirePool.Register(obj, false);
            }

            var poolBase = (ObjectPoolBase)expirePool;
            float now = Time.realtimeSinceStartup + 9999f;
            int wakes = 0;

            Stopwatch sw = Stopwatch.StartNew();
            poolBase.ExecuteMaintenance(now, false);
            wakes++;
            while (poolBase.Count > 0 && wakes < POOL_SIZE)
            {
                poolBase.ExecuteMaintenance(now, false);
                wakes++;
            }
            sw.Stop();

            Assert.AreEqual(0, poolBase.Count, "all expired objects should be swept");
            Debug.Log(StringUtility.Format(
                "[GenericObjectPoolBenchmark] Expired sweep {0} objects in {1} wakes: {2:F2} ms",
                POOL_SIZE, wakes, sw.Elapsed.TotalMilliseconds));
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void RunWarmup()
        {
            for (int i = 0; i < WARMUP_ROUNDS; i++)
            {
                BenchObject obj = _pool.Spawn();
                _pool.Despawn(obj);
            }
        }

        #endregion
    }
}
