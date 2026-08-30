using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Resource
{
    /// <summary>
    /// LoadAssetCallbacks 回调函数集构造与透传契约测试。
    /// </summary>
    public sealed class ResourceCallbacksTests
    {
        private static readonly LoadAssetSuccessCallback Success = (name, asset, duration, userData) => { };
        private static readonly LoadAssetFailureCallback Failure = (name, status, error, userData) => { };
        private static readonly LoadAssetUpdateCallback Update = (name, progress, userData) => { };

        #region 构造重载 [CONSTRUCTOR OVERLOADS]

        [Test]
        public void Constructor_SuccessOnly_SuccessCallbackSet()
        {
            var callbacks = new LoadAssetCallbacks(Success);

            Assert.AreSame(Success, callbacks.LoadAssetSuccessCallback);
            Assert.IsNull(callbacks.LoadAssetFailureCallback);
            Assert.IsNull(callbacks.LoadAssetUpdateCallback);
        }

        [Test]
        public void Constructor_SuccessAndFailure_BothSet()
        {
            var callbacks = new LoadAssetCallbacks(Success, Failure);

            Assert.AreSame(Success, callbacks.LoadAssetSuccessCallback);
            Assert.AreSame(Failure, callbacks.LoadAssetFailureCallback);
            Assert.IsNull(callbacks.LoadAssetUpdateCallback);
        }

        [Test]
        public void Constructor_SuccessAndUpdate_BothSet()
        {
            var callbacks = new LoadAssetCallbacks(Success, Update);

            Assert.AreSame(Success, callbacks.LoadAssetSuccessCallback);
            Assert.IsNull(callbacks.LoadAssetFailureCallback);
            Assert.AreSame(Update, callbacks.LoadAssetUpdateCallback);
        }

        [Test]
        public void Constructor_AllThree_AllSet()
        {
            var callbacks = new LoadAssetCallbacks(Success, Failure, Update);

            Assert.AreSame(Success, callbacks.LoadAssetSuccessCallback);
            Assert.AreSame(Failure, callbacks.LoadAssetFailureCallback);
            Assert.AreSame(Update, callbacks.LoadAssetUpdateCallback);
        }

        #endregion

        #region 校验 [VALIDATION]

        [Test]
        public void Constructor_NullSuccess_ThrowsGameException()
        {
            Assert.Throws<GameException>(() => new LoadAssetCallbacks(null));
            Assert.Throws<GameException>(() => new LoadAssetCallbacks(null, Failure));
            Assert.Throws<GameException>(() => new LoadAssetCallbacks(null, Update));
            Assert.Throws<GameException>(() => new LoadAssetCallbacks(null, Failure, Update));
        }

        [Test]
        public void Constructor_NullFailureAndUpdate_Accepted()
        {
            Assert.DoesNotThrow(() => new LoadAssetCallbacks(Success, null, null));
        }

        #endregion

        #region 状态枚举 [STATUS ENUM]

        [Test]
        public void ELoadResourceStatus_MatchesValues()
        {
            Assert.AreEqual(0, (byte)ELoadResourceStatus.Success);
            Assert.AreEqual(1, (byte)ELoadResourceStatus.NotExist);
            Assert.AreEqual(2, (byte)ELoadResourceStatus.NotReady);
            Assert.AreEqual(3, (byte)ELoadResourceStatus.DependencyError);
            Assert.AreEqual(4, (byte)ELoadResourceStatus.TypeError);
            Assert.AreEqual(5, (byte)ELoadResourceStatus.AssetError);
        }

        #endregion
    }
}
