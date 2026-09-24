using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// 加载去重完成源契约：Complete 一次唤醒全部等待者，不再 while-Yield 空转。
    /// </summary>
    public sealed class LoadingOperationStateCompletionTests
    {
        [Test]
        public void WaitAsync_BeforeComplete_CompletesWithSuccessFlag()
        {
            var state = new LoadingOperationState();
            state.AddWaiter();

            UniTask<bool> wait = state.WaitAsync();
            Assert.AreEqual(UniTaskStatus.Pending, wait.Status, "未 Complete 前等待不得同步完成");

            state.Complete(true);

            Assert.IsTrue(wait.GetAwaiter().GetResult());
            Assert.IsTrue(state.IsDone);
            Assert.IsTrue(state.Succeeded);
        }

        [Test]
        public void WaitAsync_MultipleWaiters_ShareSameOutcome()
        {
            var state = new LoadingOperationState();
            state.AddWaiter();
            state.AddWaiter();

            UniTask<bool> first = state.WaitAsync();
            UniTask<bool> second = state.WaitAsync();

            state.Complete(false);

            Assert.IsFalse(first.GetAwaiter().GetResult());
            Assert.IsFalse(second.GetAwaiter().GetResult(), "两个等待者必须拿到同一结果");
        }

        [Test]
        public void WaitAsync_AfterComplete_ReturnsSucceededWithoutSource()
        {
            var state = new LoadingOperationState();
            state.Complete(true);

            Assert.IsTrue(state.WaitAsync().GetAwaiter().GetResult());
        }
    }
}
