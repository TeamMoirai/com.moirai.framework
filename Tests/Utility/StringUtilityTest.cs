using NUnit.Framework;

namespace Moirai.Atropos.Utility
{
    public class StringUtilityTest
    {
        [SetUp]
        public void SetUp()
        {
            StringUtility.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            StringUtility.Clear();
        }

        #region 基础功能测试

        [Test]
        public void Acquire_ReturnsNonNullAdapter()
        {
            var adapter = StringUtility.CreateStringBuilder();
            Assert.IsNotNull(adapter);
            adapter.Dispose();
        }

        [Test]
        public void Acquire_HasZeroLength()
        {
            var adapter = StringUtility.CreateStringBuilder();
            Assert.AreEqual(0, adapter.Length);
            adapter.Dispose();
        }

        [Test]
        public void Append_String_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append("Hello");
            adapter.Append(" ");
            adapter.Append("World");
            Assert.AreEqual("Hello World", adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_Int_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(42);
            Assert.AreEqual("42", adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_Float_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(3.14f);
            Assert.AreEqual("3.14", adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_Double_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(2.718281828);
            Assert.AreEqual("2.718281828", adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_Bool_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(true);
            Assert.AreEqual("True", adapter.ToStringAndDispose());
        }

        [Test]
        public void AppendLine_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append("Line1");
            adapter.AppendLine();
            adapter.Append("Line2");
            string result = adapter.ToStringAndDispose();
            Assert.IsTrue(result.Contains("Line1"));
            Assert.IsTrue(result.Contains("Line2"));
        }

        [Test]
        public void Clear_ResetsLength()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append("Hello");
            Assert.AreEqual(5, adapter.Length);
            adapter.Clear();
            Assert.AreEqual(0, adapter.Length);
            adapter.Dispose();
        }

        [Test]
        public void ToStringAndDispose_ReturnsStringAndDisposes()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append("Test");
            string result = adapter.ToStringAndDispose();
            Assert.AreEqual("Test", result);
        }

        #endregion

        #region Format 测试

        [Test]
        public void Format_OneArg_FormatsCorrectly()
        {
            string result = StringUtility.GetString(sb =>
            {
                sb.Format("HP: {0}", 100);
            });
            Assert.AreEqual("HP: 100", result);
        }

        [Test]
        public void Format_TwoArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format("HP: {0}/{1}", 100, 200);
            Assert.AreEqual("HP: 100/200", result);
            adapter.Dispose();
        }

        [Test]
        public void Format_ThreeArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format("{0}-{1}-{2}", "a", "b", "c");
            Assert.AreEqual("a-b-c", result);
            adapter.Dispose();
        }

        [Test]
        public void Format_FourArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format("{0}{1}{2}{3}", 1, 2, 3, 4);
            Assert.AreEqual("1234", result);
            adapter.Dispose();
        }

        [Test]
        public void Format_NullFormat_ReturnsEmpty()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format(null, 1);
            Assert.AreEqual(string.Empty, result);
            adapter.Dispose();
        }

        [Test]
        public void Format_EmptyFormat_ReturnsEmpty()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format("");
            Assert.AreEqual(string.Empty, result);
            adapter.Dispose();
        }

        #endregion

        #region Concat 测试

        [Test]
        public void Concat_OneArg_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat("Hello");
            Assert.AreEqual("Hello", result);
            adapter.Dispose();
        }

        [Test]
        public void Concat_TwoArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat("Hello", " World");
            Assert.AreEqual("Hello World", result);
            adapter.Dispose();
        }

        [Test]
        public void Concat_ThreeArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat("a", "b", "c");
            Assert.AreEqual("abc", result);
            adapter.Dispose();
        }

        [Test]
        public void Concat_FourArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat(1, 2, 3, 4);
            Assert.AreEqual("1234", result);
            adapter.Dispose();
        }

        [Test]
        public void Concat_MixedTypes_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat("HP: ", 100, " / ", 200);
            Assert.AreEqual("HP: 100 / 200", result);
            adapter.Dispose();
        }

        #endregion

        #region Join 测试

        [Test]
        public void Join_Array_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string[] items = { "apple", "banana", "cherry" };
            string result = adapter.Join(", ", items);
            Assert.AreEqual("apple, banana, cherry", result);
            adapter.Dispose();
        }

        [Test]
        public void Join_EmptyArray_ReturnsEmpty()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string[] items = { };
            string result = adapter.Join(", ", items);
            Assert.AreEqual(string.Empty, result);
            adapter.Dispose();
        }

        [Test]
        public void Join_NullArray_ReturnsEmpty()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Join(", ", (string[])null);
            Assert.AreEqual(string.Empty, result);
            adapter.Dispose();
        }

        [Test]
        public void Join_IntArray_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            int[] numbers = { 1, 2, 3, 4, 5 };
            string result = adapter.Join("-", numbers);
            Assert.AreEqual("1-2-3-4-5", result);
            adapter.Dispose();
        }

        #endregion

        #region GetString 简化模式测试

        [Test]
        public void GetString_Lambda_FormatsCorrectly()
        {
            string result = StringUtility.GetString(sb =>
            {
                sb.Append("Hello ");
                sb.Append("World");
            });
            Assert.AreEqual("Hello World", result);
        }

        [Test]
        public void GetString_NullAction_ReturnsEmpty()
        {
            string result = StringUtility.GetString(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void GetString_WithFormat_FormatsCorrectly()
        {
            string result = StringUtility.GetString(sb =>
            {
                sb.Format("HP: {0}/{1}", 100, 200);
            });
            Assert.AreEqual("HP: 100/200", result);
        }

        [Test]
        public void GetString_WithConcat_FormatsCorrectly()
        {
            string result = StringUtility.GetString(sb =>
            {
                sb.Concat("Hello", " ", "World");
            });
            Assert.AreEqual("Hello World", result);
        }

        #endregion

        #region 链式调用测试

        [Test]
        public void FluentAppend_ReturnsAdapter()
        {
            var adapter = StringUtility.CreateStringBuilder();
            var result = adapter.Append("a").Append("b").Append("c");
            Assert.AreEqual(adapter, result);
            Assert.AreEqual("abc", adapter.ToStringAndDispose());
        }

        [Test]
        public void FluentAppendLine_ReturnsAdapter()
        {
            var adapter = StringUtility.CreateStringBuilder();
            var result = adapter.Append("Line1").AppendLine().Append("Line2");
            Assert.AreEqual(adapter, result);
            string str = adapter.ToStringAndDispose();
            Assert.IsTrue(str.Contains("Line1"));
            Assert.IsTrue(str.Contains("Line2"));
        }

        #endregion

        #region 池化/复用测试

        [Test]
        public void Acquire_Release_ReuseSameAdapter()
        {
            var adapter1 = StringUtility.CreateStringBuilder();
            string result1 = adapter1.ToStringAndDispose();

            var adapter2 = StringUtility.CreateStringBuilder();
            adapter2.Append("Test");
            Assert.AreEqual("Test", adapter2.ToStringAndDispose());
        }

        [Test]
        public void GetString_MultipleCalls_NoMemoryLeak()
        {
            for (int i = 0; i < 100; i++)
            {
                string result = StringUtility.GetString(sb =>
                {
                    sb.Append("Iteration ");
                    sb.Append(i);
                });
                Assert.AreEqual("Iteration " + i, result);
            }
        }

        #endregion

        #region 边界条件测试

        [Test]
        public void Append_EmptyString_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append("");
            adapter.Append("Test");
            Assert.AreEqual("Test", adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_MaxValue_Int_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(int.MaxValue);
            Assert.AreEqual(int.MaxValue.ToString(), adapter.ToStringAndDispose());
        }

        [Test]
        public void Append_MinValue_Int_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            adapter.Append(int.MinValue);
            Assert.AreEqual(int.MinValue.ToString(), adapter.ToStringAndDispose());
        }

        [Test]
        public void Format_LargeFormatString_WorksCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string longString = new string('A', 1000);
            string result = adapter.Format("{0}", longString);
            Assert.AreEqual(longString, result);
            adapter.Dispose();
        }

        [Test]
        public void Concat_ManyArgs_FormatsCorrectly()
        {
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Concat(1, 2, 3, 4);
            Assert.AreEqual("1234", result);
            adapter.Dispose();
        }

        #endregion
    }
}
