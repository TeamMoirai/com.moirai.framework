using System;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 验证 <see cref="MoiraiException"/> 各构造重载对消息与内部异常的设置，以及可分别以基类 <see cref="Exception"/> 与派生类型捕获的行为。
    /// </summary>
    public class GameExceptionTests
    {
        [Test]
        public void DefaultConstructor_NoMessage()
        {
            var ex = new MoiraiException();
            Assert.IsNotNull(ex);
            Assert.IsInstanceOf<Exception>(ex);
        }

        [Test]
        public void MessageConstructor_SetsMessage()
        {
            var ex = new MoiraiException("test error");
            Assert.AreEqual("test error", ex.Message);
        }

        [Test]
        public void InnerExceptionConstructor_SetsInnerException()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new MoiraiException("outer", inner);

            Assert.AreEqual("outer", ex.Message);
            Assert.AreSame(inner, ex.InnerException);
        }

        [Test]
        public void CanBeCaughtAsException()
        {
            bool caught = false;
            try
            {
                throw new MoiraiException("test");
            }
            catch (Exception)
            {
                caught = true;
            }

            Assert.IsTrue(caught);
        }

        [Test]
        public void CanBeCaughtAsGameException()
        {
            string message = null;
            try
            {
                throw new MoiraiException("specific");
            }
            catch (MoiraiException ex)
            {
                message = ex.Message;
            }

            Assert.AreEqual("specific", message);
        }
    }
}
