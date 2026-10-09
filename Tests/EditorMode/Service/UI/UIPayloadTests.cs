using System;
using Moirai.Atropos;
using Moirai.Atropos.UI;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>UIPayload：Empty 与 null 同判；引用型零分配直存；失败面一律 GameException 带期望类型名。</summary>
    [TestFixture]
    public sealed class UIPayloadTests
    {
        [Test]
        public void From_ClassValue_StoresReferenceWithoutBoxing()
        {
            var dto = new object();
            var payload = UIPayload.From(dto);
            Assert.IsFalse(payload.IsEmpty);
            Assert.AreSame(dto, payload.To<object>(), "引用型只存引用");
        }

        [Test]
        public void From_Null_ReducesToEmpty()
        {
            Assert.IsTrue(UIPayload.From(null).IsEmpty, "null 归约为 Empty");
            Assert.IsTrue(UIPayload.Empty.IsEmpty);
            Assert.IsTrue(default(UIPayload).IsEmpty);
        }

        [Test]
        public void To_WrongType_ThrowsGameExceptionWithExpectedName()
        {
            var payload = UIPayload.From("text");
            var ex = Assert.Throws<GameException>(() => payload.To<int>());
            StringAssert.Contains(nameof(Int32), ex.Message, "消息带期望类型名");
        }

        [Test]
        public void To_EmptyOnValueType_ThrowsGameException()
        {
            var ex = Assert.Throws<GameException>(() => UIPayload.Empty.To<int>());
            StringAssert.Contains(nameof(Int32), ex.Message);
        }

        [Test]
        public void To_EmptyOnReferenceType_ReturnsNull()
        {
            Assert.IsNull(UIPayload.Empty.To<string>(), "Empty+引用型回 null");
        }

        [Test]
        public void TryGet_MismatchOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(UIPayload.From("text").TryGet<int>(out _));
            Assert.IsFalse(UIPayload.Empty.TryGet<string>(out _));
            Assert.IsTrue(UIPayload.From(7).TryGet<int>(out var seven) && seven == 7);
        }
    }
}
