using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos;
using NUnit.Framework;
using Mp = Moirai.Atropos.MemoryPool;

namespace Core.MemoryPool
{
    /// <summary>
    /// 可编排行为的池化对象：Clear / OnEvict 上挂用户回调并累计次数，供断言"回调被叫了几回、叫没叫错对象"。
    /// </summary>
    internal class PoolItem : MemoryObject, IPoolEvictable
    {
        public object Payload;
        public Action OnClear;
        public Action OnEviction;
        public int Clears;
        public int Evictions;

        public override void Clear()
        {
            OnClear?.Invoke();
            Payload = null;
            Clears++;
        }

        public void OnEvict()
        {
            OnEviction?.Invoke();
            Evictions++;
        }
    }

    /// <summary>
    /// 第二个可编排类型：跨池归属与"一个池的回调拖垮整轮维护"用例需要两个互不相干的池。
    /// </summary>
    internal sealed class OtherItem : PoolItem
    {
    }

    /// <summary>
    /// 泛型占位类型：按不同 T 闭合可批量造出互不注册的池，用来压注册表与活跃调度表。
    /// </summary>
    internal sealed class ColdItem<T> : MemoryObject
    {
        public override void Clear()
        {
        }
    }

    /// <summary>
    /// 构造行为可注入的类型：Construct 里抛异常即可模拟"构造函数失败"，用来验池没吃掉槽位与计数。
    /// <para>递归封顶是必需的：构造期重入取用一旦失去护栏，`new T()` → Add → `new T()` 会当场
    /// 把宿主（编辑器）以栈溢出方式打崩，而不是把缺陷报成一格红。</para>
    /// </summary>
    internal sealed class ConstructorItem : MemoryObject
    {
        public static Action Construct;
        private static int s_depth;

        public ConstructorItem()
        {
            if (Construct == null)
            {
                return;
            }

            if (++s_depth > 8)
            {
                s_depth = 0;
                throw new InvalidOperationException("构造期重入未被拦截（递归已封顶）");
            }

            try
            {
                Construct();
            }
            finally
            {
                s_depth--;
            }
        }

        public override void Clear()
        {
        }
    }

    /// <summary>
    /// 内存池用例基座：为每个用到的类型重置池、跑独立帧号的 Tick，并在 TearDown 里
    /// 侦测未归还的租约、清空对应用途的池、还原全局旋钮。
    /// <para>
    /// 池是类型级全局单例，进程内所有夹具共享同一份状态。一个用例漏还一只对象，
    /// 下一个用例会读到虚高的 UsingCount，而 ClearAll 并不能纠正它（在外的对象本就该活着），
    /// 于是表现为"单独跑绿、整套跑红"。这里把这种污染当场钉死在用例自己的红上。
    /// </para>
    /// </summary>
    public abstract class MemoryPoolFixture
    {
        private readonly HashSet<Type> _types = new HashSet<Type>();
        private int _shortDecay;
        private int _longDecay;
        private int _zeroReserve;
        private int _unschedule;
        private int _autoTrim;
        private EMemoryPoolPhase _phase;

        /// <summary>
        /// Tick 用的帧号游标。EditMode 下 Time.frameCount 不推进，必须自带递增帧号才能真正走完 Tick 分支。
        /// </summary>
        protected int Frame;

        [SetUp]
        public void SetUpPool()
        {
            _shortDecay = Mp.ShortDecayStartFrames;
            _longDecay = Mp.LongDecayStartFrames;
            _zeroReserve = Mp.ZeroFreeReserveStartFrames;
            _unschedule = Mp.UnscheduleIdleFrames;
            _autoTrim = Mp.AutoTrimNativeMetadataFrames;
            _phase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Gameplay;
            Frame = MemoryPoolRegistry.CurrentFrame + 100;
            Use<PoolItem>();
            Use<OtherItem>();
            Use<ConstructorItem>();
        }

        /// <summary>
        /// 声明本用例要动的类型，并把它复位成已知状态：无残留租约、清空内容、给足容量、统计归零。
        /// </summary>
        protected void Use<T>() where T : MemoryObject, new()
        {
            _types.Add(typeof(T));
            Assert.AreEqual(0, Info<T>().UsingCount, typeof(T).Name + " 带着上个用例未归还的租约");
            MemoryPool<T>.ClearAll();
            MemoryPool<T>.SetCapacity(128, 512);
            MemoryPool<T>.ResetStats();
        }

        protected void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                MemoryPoolRegistry.TickAll(++Frame);
            }
        }

        protected static MemoryPoolInfo Info<T>() where T : MemoryObject, new()
        {
            MemoryPoolInfo info = default;
            MemoryPool<T>.GetInfo(ref info);
            return info;
        }

        protected static MemoryPoolInfo Info(MemoryPoolHandle handle)
        {
            MemoryPoolInfo info = default;
            handle.Inner.GetInfo(ref info);
            return info;
        }

        /// <summary>
        /// 读取私有静态字段（如 s_PageCapacity）：Info 里的 PageCapacity 分不清"页都还挂着"与
        /// "页已交还回收栈"，需要直读产码字段才能判真。
        /// </summary>
        protected static TField StaticField<TField>(Type owner, string name)
        {
            FieldInfo field = owner.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{owner.Name} 上找不到字段 {name}（产码改名后要同步本用例）");
            return (TField)field.GetValue(null);
        }

        [TearDown]
        public void TearDownPool()
        {
            List<Exception> errors = null;
            try
            {
                foreach (Type type in _types)
                {
                    try
                    {
                        MemoryPoolHandle handle = Mp.GetHandle(type);
                        Assert.AreEqual(0, Info(handle).UsingCount, type.Name + " 有未归还的租约");
                    }
                    catch (Exception error)
                    {
                        errors = Collect(errors, error);
                    }

                    try
                    {
                        Mp.RemoveAll(type);
                    }
                    catch (Exception error)
                    {
                        errors = Collect(errors, error);
                    }
                }
            }
            finally
            {
                _types.Clear();
                ConstructorItem.Construct = null;
                Mp.ShortDecayStartFrames = _shortDecay;
                Mp.LongDecayStartFrames = _longDecay;
                Mp.ZeroFreeReserveStartFrames = _zeroReserve;
                Mp.UnscheduleIdleFrames = _unschedule;
                Mp.AutoTrimNativeMetadataFrames = _autoTrim;
                MemoryPoolRegistry.Phase = _phase;
            }

            if (errors != null)
            {
                throw new AggregateException(errors);
            }
        }

        private static List<Exception> Collect(List<Exception> errors, Exception error)
        {
            errors ??= new List<Exception>();
            errors.Add(error);
            return errors;
        }

        /// <summary>
        /// 执行动作并返回它抛出的异常，不抛即判失败。
        /// </summary>
        protected static Exception CatchThrows(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                return exception;
            }

            Assert.Fail("未抛出异常");
            return null;
        }

        /// <summary>
        /// 沿 InnerException / AggregateException 链找消息片段。护栏与池自身的报错常被上一层包装
        /// （Clear() failed、构造反射），只看顶层消息会随运行时不同而误判。
        /// </summary>
        protected static bool Mentions(Exception exception, string fragment)
        {
            return Walk(exception, fragment, 0) != null;
        }

        /// <summary>
        /// 判断链上是否挂着指定的那个异常实例（用于"原始原因不能被包装掉"的断言）。
        /// </summary>
        protected static bool CausedBy(Exception exception, Exception cause)
        {
            return WalkCause(exception, cause, 0);
        }

        private static Exception Walk(Exception exception, string fragment, int depth)
        {
            if (exception == null || depth > 8)
            {
                return null;
            }

            if (exception.Message != null && exception.Message.Contains(fragment))
            {
                return exception;
            }

            Exception match = Walk(exception.InnerException, fragment, depth + 1);
            if (match != null)
            {
                return match;
            }

            if (exception is AggregateException aggregate)
            {
                for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                {
                    match = Walk(aggregate.InnerExceptions[i], fragment, depth + 1);
                    if (match != null)
                    {
                        return match;
                    }
                }
            }

            return null;
        }

        private static bool WalkCause(Exception exception, Exception cause, int depth)
        {
            if (exception == null || depth > 8)
            {
                return false;
            }

            if (ReferenceEquals(exception, cause))
            {
                return true;
            }

            if (WalkCause(exception.InnerException, cause, depth + 1))
            {
                return true;
            }

            if (exception is AggregateException aggregate)
            {
                for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                {
                    if (WalkCause(aggregate.InnerExceptions[i], cause, depth + 1))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 基座自身的回归：租约没还得让用例红，同时全局旋钮仍要还原，否则污染会外溢到其它夹具。
    /// </summary>
    public sealed class MemoryPoolFixtureTests
    {
        private sealed class Fixture : MemoryPoolFixture
        {
        }

        [Test]
        public void LeakedLeaseFailsTeardownAndStillRestoresGlobalSettings()
        {
            Fixture fixture = new Fixture();
            fixture.SetUpPool();
            int decay = Mp.ShortDecayStartFrames;
            Mp.ShortDecayStartFrames = decay + 1;
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            try
            {
                Exception exception = Assert.Throws<AggregateException>(() => fixture.TearDownPool());
                Assert.AreEqual(1, ((AggregateException)exception).InnerExceptions.Count);
                StringAssert.Contains("未归还的租约", exception.InnerException.Message);
                Assert.AreEqual(decay, Mp.ShortDecayStartFrames, "TearDown 抛异常前没还原全局旋钮");
            }
            finally
            {
                MemoryPool<PoolItem>.Release(item);
                MemoryPool<PoolItem>.ClearAll();
            }
        }
    }
}
