using System;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
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
            var adapter = StringUtility.CreateStringBuilder();
            string result = adapter.Format("HP: {0}", 100);
            Assert.AreEqual("HP: 100", result);
            adapter.Dispose();
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

        #region 静态 Format 测试 [STATIC FORMAT TESTS]

        // IStringBuilder.Format/Concat 直接返回结果字符串、不写入构建器内容——静态外观是等价路径。

        [Test]
        public void Static_Format_TwoArgs_FormatsCorrectly()
        {
            string result = StringUtility.Format("HP: {0}/{1}", 100, 200);
            Assert.AreEqual("HP: 100/200", result);
        }

        [Test]
        public void Static_Format_ThreeArgs_FormatsCorrectly()
        {
            string result = StringUtility.Format("{0}-{1}-{2}", "a", "b", "c");
            Assert.AreEqual("a-b-c", result);
        }

        #endregion

        #region 静态 Concat 测试 [STATIC CONCAT TESTS]

        [Test]
        public void Static_Concat_OneArg()
        {
            Assert.AreEqual("Hello", StringUtility.Concat("Hello"));
        }

        [Test]
        public void Static_Concat_TwoArgs()
        {
            Assert.AreEqual("Hello World", StringUtility.Concat("Hello", " World"));
        }

        [Test]
        public void Static_Concat_ThreeArgs()
        {
            Assert.AreEqual("abc", StringUtility.Concat("a", "b", "c"));
        }

        [Test]
        public void Static_Concat_FourArgs()
        {
            Assert.AreEqual("1234", StringUtility.Concat(1, 2, 3, 4));
        }

        [Test]
        public void Static_Concat_MixedTypes()
        {
            Assert.AreEqual("HP: 100 / 200", StringUtility.Concat("HP: ", 100, " / ", 200));
        }

        [Test]
        public void Static_Concat_NullValue()
        {
            Assert.AreEqual(string.Empty, StringUtility.Concat<object>(null));
        }

        #endregion

        #region 静态 Join 测试 [STATIC JOIN TESTS]

        [Test]
        public void Static_Join_Array()
        {
            string[] items = { "apple", "banana", "cherry" };
            Assert.AreEqual("apple, banana, cherry", StringUtility.Join(", ", items));
        }

        [Test]
        public void Static_Join_EmptyArray()
        {
            Assert.AreEqual(string.Empty, StringUtility.Join(", ", new string[0]));
        }

        [Test]
        public void Static_Join_NullArray()
        {
            Assert.AreEqual(string.Empty, StringUtility.Join(", ", (string[])null));
        }

        [Test]
        public void Static_Join_IntArray()
        {
            int[] numbers = { 1, 2, 3, 4, 5 };
            Assert.AreEqual("1-2-3-4-5", StringUtility.Join("-", numbers));
        }

        [Test]
        public void Static_Join_Span()
        {
            ReadOnlySpan<int> span = stackalloc int[] { 1, 2, 3 };
            Assert.AreEqual("1, 2, 3", StringUtility.Join(", ", span));
        }

        #endregion

        #region 静态 Insert 测试 [STATIC INSERT TESTS]

        [Test]
        public void Static_Insert_String()
        {
            Assert.AreEqual("He[llo]llo", StringUtility.Insert("Hello", 2, "[llo]"));
        }

        [Test]
        public void Static_Insert_AtStart()
        {
            Assert.AreEqual("Prefix-Hello", StringUtility.Insert("Hello", 0, "Prefix-"));
        }

        [Test]
        public void Static_Insert_AtEnd()
        {
            Assert.AreEqual("Hello-Suffix", StringUtility.Insert("Hello", 5, "-Suffix"));
        }

        [Test]
        public void Static_Insert_Char()
        {
            Assert.AreEqual("HeXllo", StringUtility.Insert("Hello", 2, 'X'));
        }

        [Test]
        public void Static_Insert_StringWithCount()
        {
            Assert.AreEqual("HeXXllo", StringUtility.Insert("Hello", 2, "X", 2));
        }

        [Test]
        public void Static_Insert_NullSource_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, StringUtility.Insert(null, 0, "test"));
        }

        #endregion

        #region 静态 Remove 测试 [STATIC REMOVE TESTS]

        [Test]
        public void Static_Remove_Middle()
        {
            Assert.AreEqual("Heo", StringUtility.Remove("Hello", 2, 2));
        }

        [Test]
        public void Static_Remove_FromStart()
        {
            Assert.AreEqual("llo", StringUtility.Remove("Hello", 0, 2));
        }

        [Test]
        public void Static_Remove_FromEnd()
        {
            Assert.AreEqual("Hel", StringUtility.Remove("Hello", 3, 2));
        }

        [Test]
        public void Static_Remove_All()
        {
            Assert.AreEqual(string.Empty, StringUtility.Remove("Hello", 0, 5));
        }

        [Test]
        public void Static_Remove_NullSource_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, StringUtility.Remove(null, 0, 1));
        }

        #endregion

        #region 静态 Replace 测试 [STATIC REPLACE TESTS]

        [Test]
        public void Static_Replace_Char()
        {
            Assert.AreEqual("Hexxo", StringUtility.Replace("Hello", 'l', 'x'));
        }

        [Test]
        public void Static_Replace_Char_WithRange()
        {
            Assert.AreEqual("Hexlo", StringUtility.Replace("Hello", 'l', 'x', 0, 3));
        }

        [Test]
        public void Static_Replace_String()
        {
            Assert.AreEqual("Hexxo", StringUtility.Replace("Hello", "ll", "xx"));
        }

        [Test]
        public void Static_Replace_String_WithRange()
        {
            Assert.AreEqual("Hexlo", StringUtility.Replace("Hello", "l", "x", 0, 3));
        }

        [Test]
        public void Static_Replace_NoMatch_ReturnsOriginal()
        {
            Assert.AreEqual("Hello", StringUtility.Replace("Hello", 'z', 'x'));
        }

        [Test]
        public void Static_Replace_NullSource_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, StringUtility.Replace(null, 'a', 'b'));
        }

        #endregion
    }
}
