using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    public class DefaultJsonTest
    {
        #region 辅助类型 [HELPER TYPES]

        private enum TestEnum
        {
            None = 0,
            First = 1,
            Second = 2
        }

        [System.Serializable]
        private class SimpleClass
        {
            public string name;
            public int age;
            public bool active;
        }

        [System.Serializable]
        private class AllPrimitivesClass
        {
            public bool boolVal;
            public byte byteVal;
            public sbyte sbyteVal;
            public short shortVal;
            public ushort ushortVal;
            public int intVal;
            public uint uintVal;
            public long longVal;
            public ulong ulongVal;
            public float floatVal;
            public double doubleVal;
            public decimal decimalVal;
            public char charVal;
            public string stringVal;
        }

        [System.Serializable]
        private class NestedClass
        {
            public string label;
            public SimpleClass child;
        }

        [System.Serializable]
        private class CollectionClass
        {
            public List<int> intList;
            public List<string> stringList;
            public string[] stringArray;
            public int[] intArray;
            public Dictionary<string, int> dictSI;
            public Dictionary<int, string> dictIS;
            public Dictionary<string, SimpleClass> dictSObj;
        }

        [System.Serializable]
        private class EnumClass
        {
            public TestEnum enumVal;
            public TestEnum enumVal2;
        }

        [System.Serializable]
        private class NullableClass
        {
            public int? nullableInt;
            public bool? nullableBool;
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            MemoryPool.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPool.ClearAll();
        }

        #region 基础类型测试 [PRIMITIVE TYPES]

        [Test]
        public void Bool_True_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual(true, result.active);
        }

        [Test]
        public void Bool_False_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 1, active = false };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual(false, result.active);
        }

        [Test]
        public void Int_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 42, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual(42, result.age);
        }

        [Test]
        public void Int_Negative_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = -100, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual(-100, result.age);
        }

        [Test]
        public void Int_Zero_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 0, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual(0, result.age);
        }

        [Test]
        public void String_RoundTrip()
        {
            var obj = new SimpleClass { name = "hello world", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual("hello world", result.name);
        }

        [Test]
        public void String_Empty_RoundTrip()
        {
            var obj = new SimpleClass { name = "", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual("", result.name);
        }

        [Test]
        public void String_WithSpecialChars_RoundTrip()
        {
            var obj = new SimpleClass { name = "a\"b\\c\nd\te", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual("a\"b\\c\nd\te", result.name);
        }

        [Test]
        public void String_Unicode_RoundTrip()
        {
            var obj = new SimpleClass { name = "你好世界🌍", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<SimpleClass>(json);
            Assert.AreEqual("你好世界🌍", result.name);
        }

        #endregion

        #region 数值类型测试 [NUMERIC TYPES]

        [Test]
        public void AllPrimitives_RoundTrip()
        {
            var obj = new AllPrimitivesClass
            {
                boolVal = true,
                byteVal = 255,
                sbyteVal = -128,
                shortVal = 32767,
                ushortVal = 65535,
                intVal = 2147483647,
                uintVal = 4294967295,
                longVal = 9223372036854775807,
                ulongVal = 18446744073709551615,
                floatVal = 3.14f,
                doubleVal = 2.718281828459045,
                decimalVal = 123.456m,
                charVal = 'A',
                stringVal = "test"
            };

            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);

            Assert.AreEqual(true, result.boolVal);
            Assert.AreEqual(255, result.byteVal);
            Assert.AreEqual(-128, result.sbyteVal);
            Assert.AreEqual(32767, result.shortVal);
            Assert.AreEqual(65535, result.ushortVal);
            Assert.AreEqual(2147483647, result.intVal);
            Assert.AreEqual(4294967295, result.uintVal);
            Assert.AreEqual(9223372036854775807, result.longVal);
            Assert.AreEqual(18446744073709551615, result.ulongVal);
            Assert.AreEqual(3.14f, result.floatVal, 0.001f);
            Assert.AreEqual(2.718281828459045, result.doubleVal, 0.000001);
            Assert.AreEqual(123.456m, result.decimalVal);
            Assert.AreEqual('A', result.charVal);
            Assert.AreEqual("test", result.stringVal);
        }

        [Test]
        public void Float_RoundTrip()
        {
            var obj = new AllPrimitivesClass { floatVal = 1.5f };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.AreEqual(1.5f, result.floatVal, 0.001f);
        }

        [Test]
        public void Double_RoundTrip()
        {
            var obj = new AllPrimitivesClass { doubleVal = 123.456789 };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.AreEqual(123.456789, result.doubleVal, 0.000001);
        }

        [Test]
        public void Decimal_RoundTrip()
        {
            var obj = new AllPrimitivesClass { decimalVal = 99.99m };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.AreEqual(99.99m, result.decimalVal);
        }

        [Test]
        public void Long_MaxValue_RoundTrip()
        {
            var obj = new AllPrimitivesClass { longVal = long.MaxValue };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.AreEqual(long.MaxValue, result.longVal);
        }

        [Test]
        public void Long_MinValue_RoundTrip()
        {
            var obj = new AllPrimitivesClass { longVal = long.MinValue };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.AreEqual(long.MinValue, result.longVal);
        }

        #endregion

        #region 枚举类型测试 [ENUM TYPES]

        [Test]
        public void Enum_RoundTrip()
        {
            var obj = new EnumClass { enumVal = TestEnum.Second, enumVal2 = TestEnum.None };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<EnumClass>(json);
            Assert.AreEqual(TestEnum.Second, result.enumVal);
            Assert.AreEqual(TestEnum.None, result.enumVal2);
        }

        [Test]
        public void Enum_FirstValue_RoundTrip()
        {
            var obj = new EnumClass { enumVal = TestEnum.First };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<EnumClass>(json);
            Assert.AreEqual(TestEnum.First, result.enumVal);
        }

        #endregion

        #region 集合类型测试 [COLLECTION TYPES]

        [Test]
        public void List_Int_RoundTrip()
        {
            var obj = new CollectionClass { intList = new List<int> { 1, 2, 3, 4, 5 } };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.intList);
            Assert.AreEqual(5, result.intList.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.intList);
        }

        [Test]
        public void List_String_RoundTrip()
        {
            var obj = new CollectionClass { stringList = new List<string> { "a", "b", "c" } };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.stringList);
            Assert.AreEqual(3, result.stringList.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result.stringList);
        }

        [Test]
        public void List_Empty_RoundTrip()
        {
            var obj = new CollectionClass { intList = new List<int>() };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.intList);
            Assert.AreEqual(0, result.intList.Count);
        }

        [Test]
        public void Array_Int_RoundTrip()
        {
            var obj = new CollectionClass { intArray = new int[] { 10, 20, 30 } };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.intArray);
            Assert.AreEqual(3, result.intArray.Length);
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, result.intArray);
        }

        [Test]
        public void Array_String_RoundTrip()
        {
            var obj = new CollectionClass { stringArray = new string[] { "x", "y", "z" } };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.stringArray);
            Assert.AreEqual(3, result.stringArray.Length);
            CollectionAssert.AreEqual(new[] { "x", "y", "z" }, result.stringArray);
        }

        [Test]
        public void Array_Empty_RoundTrip()
        {
            var obj = new CollectionClass { intArray = new int[0] };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.intArray);
            Assert.AreEqual(0, result.intArray.Length);
        }

        [Test]
        public void Dictionary_StringInt_RoundTrip()
        {
            var obj = new CollectionClass
            {
                dictSI = new Dictionary<string, int> { { "a", 1 }, { "b", 2 }, { "c", 3 } }
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.dictSI);
            Assert.AreEqual(3, result.dictSI.Count);
            Assert.AreEqual(1, result.dictSI["a"]);
            Assert.AreEqual(2, result.dictSI["b"]);
            Assert.AreEqual(3, result.dictSI["c"]);
        }

        [Test]
        public void Dictionary_IntString_RoundTrip()
        {
            var obj = new CollectionClass
            {
                dictIS = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } }
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.dictIS);
            Assert.AreEqual(2, result.dictIS.Count);
            Assert.AreEqual("one", result.dictIS[1]);
            Assert.AreEqual("two", result.dictIS[2]);
        }

        [Test]
        public void Dictionary_StringObject_RoundTrip()
        {
            var obj = new CollectionClass
            {
                dictSObj = new Dictionary<string, SimpleClass>
                {
                    { "first", new SimpleClass { name = "Alice", age = 30, active = true } },
                    { "second", new SimpleClass { name = "Bob", age = 25, active = false } }
                }
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.dictSObj);
            Assert.AreEqual(2, result.dictSObj.Count);
            Assert.AreEqual("Alice", result.dictSObj["first"].name);
            Assert.AreEqual(30, result.dictSObj["first"].age);
            Assert.AreEqual("Bob", result.dictSObj["second"].name);
            Assert.AreEqual(false, result.dictSObj["second"].active);
        }

        [Test]
        public void Dictionary_Empty_RoundTrip()
        {
            var obj = new CollectionClass { dictSI = new Dictionary<string, int>() };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.IsNotNull(result.dictSI);
            Assert.AreEqual(0, result.dictSI.Count);
        }

        #endregion

        #region 嵌套对象测试 [NESTED OBJECTS]

        [Test]
        public void NestedObject_RoundTrip()
        {
            var obj = new NestedClass
            {
                label = "parent",
                child = new SimpleClass { name = "child", age = 10, active = false }
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<NestedClass>(json);
            Assert.AreEqual("parent", result.label);
            Assert.IsNotNull(result.child);
            Assert.AreEqual("child", result.child.name);
            Assert.AreEqual(10, result.child.age);
            Assert.AreEqual(false, result.child.active);
        }

        [Test]
        public void List_OfObjects_RoundTrip()
        {
            var list = new List<SimpleClass>
            {
                new SimpleClass { name = "a", age = 1, active = true },
                new SimpleClass { name = "b", age = 2, active = false }
            };
            string json = DefaultJson.ToJson(list);
            var result = DefaultJson.FromJson<List<SimpleClass>>(json);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("a", result[0].name);
            Assert.AreEqual("b", result[1].name);
        }

        [Test]
        public void Array_OfObjects_RoundTrip()
        {
            var arr = new SimpleClass[]
            {
                new SimpleClass { name = "x", age = 10, active = true },
                new SimpleClass { name = "y", age = 20, active = false }
            };
            string json = DefaultJson.ToJson(arr);
            var result = DefaultJson.FromJson<SimpleClass[]>(json);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("x", result[0].name);
            Assert.AreEqual("y", result[1].name);
        }

        #endregion

        #region Null 处理测试 [NULL HANDLING]

        [Test]
        public void Null_String_RemovedWhenRemoveNulls()
        {
            var obj = new SimpleClass { name = null, age = 5, active = true };
            string json = DefaultJson.ToJson(obj, removeNulls: true);
            Assert.IsFalse(json.Contains("name"));
            Assert.IsTrue(json.Contains("5"));
        }

        [Test]
        public void Null_String_IncludedWhenNotRemoveNulls()
        {
            var obj = new SimpleClass { name = null, age = 5, active = true };
            string json = DefaultJson.ToJson(obj, removeNulls: false);
            Assert.IsTrue(json.Contains("name"));
            Assert.IsTrue(json.Contains("null"));
        }

        [Test]
        public void Null_Object_RoundTrip()
        {
            var obj = new NestedClass { label = "test", child = null };
            string json = DefaultJson.ToJson(obj, removeNulls: false);
            var result = DefaultJson.FromJson<NestedClass>(json);
            Assert.AreEqual("test", result.label);
        }

        #endregion

        #region FromJSONOverwrite 测试 [OVERWRITE]

        [Test]
        public void Overwrite_SingleField()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            DefaultJson.FromJsonOverwrite(obj, "{\"name\":\"new\"}");
            Assert.AreEqual("new", obj.name);
            Assert.AreEqual(1, obj.age);
            Assert.AreEqual(false, obj.active);
        }

        [Test]
        public void Overwrite_AllFields()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            DefaultJson.FromJsonOverwrite(obj, "{\"name\":\"new\",\"age\":99,\"active\":true}");
            Assert.AreEqual("new", obj.name);
            Assert.AreEqual(99, obj.age);
            Assert.AreEqual(true, obj.active);
        }

        [Test]
        public void Overwrite_PartialFields()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            DefaultJson.FromJsonOverwrite(obj, "{\"age\":42}");
            Assert.AreEqual("old", obj.name);
            Assert.AreEqual(42, obj.age);
            Assert.AreEqual(false, obj.active);
        }

        #endregion

        #region Readable 模式测试 [READABLE MODE]

        [Test]
        public void ToJSON_Readable_ContainsWhitespace()
        {
            var obj = new SimpleClass { name = "test", age = 5, active = true };
            string json = DefaultJson.ToJson(obj, readable: true);
            Assert.IsTrue(json.Contains("\r\n"));
            Assert.IsTrue(json.Contains("\t"));
        }

        [Test]
        public void ToJSON_Compact_NoWhitespace()
        {
            var obj = new SimpleClass { name = "test", age = 5, active = true };
            string json = DefaultJson.ToJson(obj, readable: false);
            Assert.IsFalse(json.Contains("\r\n"));
            Assert.IsFalse(json.Contains("\t"));
        }

        #endregion

        #region MemoryPool 复用测试 [POOL REUSE]

        [Test]
        public void Pool_Reuses_DeserializationObject()
        {
            var obj = new SimpleClass { name = "a", age = 1, active = true };
            string json = DefaultJson.ToJson(obj);

            var r1 = DefaultJson.FromJson<SimpleClass>(json);
            var r2 = DefaultJson.FromJson<SimpleClass>(json);

            Assert.AreEqual("a", r1.name);
            Assert.AreEqual("a", r2.name);
        }

        [Test]
        public void Pool_Reuses_SerializationObject()
        {
            var obj1 = new SimpleClass { name = "a", age = 1, active = true };
            var obj2 = new SimpleClass { name = "b", age = 2, active = false };

            string json1 = DefaultJson.ToJson(obj1);
            string json2 = DefaultJson.ToJson(obj2);

            Assert.IsTrue(json1.Contains("\"name\":\"a\""));
            Assert.IsTrue(json2.Contains("\"name\":\"b\""));
            Assert.IsFalse(json2.Contains("\"name\":\"a\""));
        }

        [Test]
        public void Pool_Handles_MixedTypes()
        {
            var simple = new SimpleClass { name = "simple", age = 1, active = true };
            var nested = new NestedClass
            {
                label = "nested",
                child = new SimpleClass { name = "child", age = 2, active = false }
            };
            var collection = new CollectionClass
            {
                intList = new List<int> { 1, 2, 3 }
            };

            string json1 = DefaultJson.ToJson(simple);
            string json2 = DefaultJson.ToJson(nested);
            string json3 = DefaultJson.ToJson(collection);

            var r1 = DefaultJson.FromJson<SimpleClass>(json1);
            var r2 = DefaultJson.FromJson<NestedClass>(json2);
            var r3 = DefaultJson.FromJson<CollectionClass>(json3);

            Assert.AreEqual("simple", r1.name);
            Assert.AreEqual("nested", r2.label);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, r3.intList);
        }

        #endregion

        #region Type 非泛型测试 [NON-GENERIC TYPE]

        [Test]
        public void FromJSON_NonGeneric_Type()
        {
            var obj = new SimpleClass { name = "test", age = 42, active = true };
            string json = DefaultJson.ToJson(obj);
            object result = DefaultJson.FromJson(json, typeof(SimpleClass));
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SimpleClass>(result);
            Assert.AreEqual("test", ((SimpleClass)result).name);
            Assert.AreEqual(42, ((SimpleClass)result).age);
        }

        #endregion

        #region 商业化加固测试 [COMMERCIAL HARDENING]

        [System.Serializable]
        private class KnownTypesClass
        {
            public DateTime dateTime;
            public Guid guid;
            public TimeSpan timeSpan;
            public DateTimeOffset dateTimeOffset;
        }

        [System.Serializable]
        private class SelfReferenceClass
        {
            public string id;
            public SelfReferenceClass child;
        }

        // ===== 区域设置（序列化/解析两端必须区域性固定） =====

        [Test]
        public void Culture_DeDE_DecimalFloat_RoundTrip()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");
                var obj = new AllPrimitivesClass { decimalVal = 1234.56m, floatVal = 1.5f, doubleVal = 2.25d };
                string json = DefaultJson.ToJson(obj);
                var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
                Assert.AreEqual(1234.56m, result.decimalVal, "de-DE 下 decimal 不得损坏");
                Assert.AreEqual(1.5f, result.floatVal, "de-DE 下 float 不得损坏");
                Assert.AreEqual(2.25d, result.doubleVal, "de-DE 下 double 不得损坏");
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        // ===== 截断/畸形输入（必须显式抛错，绝不静默丢数据） =====

        [Test]
        public void Truncated_List_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() => DefaultJson.FromJson<List<int>>("[1"));
        }

        [Test]
        public void Truncated_List_WithComma_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() => DefaultJson.FromJson<List<int>>("[1,2,3"));
        }

        [Test]
        public void Truncated_Object_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() => DefaultJson.FromJson<SimpleClass>("{\"name\":\"a\""));
        }

        [Test]
        public void Malformed_UnterminatedString_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() => DefaultJson.FromJson<SimpleClass>("{\"name\":\"abc}"));
        }

        // ===== 未知字段（前向/后向兼容，默认忽略） =====

        [Test]
        public void UnknownFields_Ignored()
        {
            var obj = DefaultJson.FromJson<SimpleClass>("{\"name\":\"ok\",\"unknownField\":2,\"another\":{\"deep\":[1,2]}}");
            Assert.AreEqual("ok", obj.name);
            Assert.AreEqual(0, obj.age);
        }

        // ===== 根原始类型（对称支持） =====

        [Test]
        public void RootPrimitive_Int_RoundTrip()
        {
            Assert.AreEqual(42, DefaultJson.FromJson<int>("42"));
            Assert.AreEqual("42", DefaultJson.ToJson(42));
        }

        [Test]
        public void RootPrimitive_String_RoundTrip()
        {
            Assert.AreEqual("hi", DefaultJson.FromJson<string>("\"hi\""));
            Assert.AreEqual("hi", DefaultJson.ToJson("hi").Trim('"'));
        }

        [Test]
        public void RootPrimitive_Bool_RoundTrip()
        {
            Assert.AreEqual(true, DefaultJson.FromJson<bool>("true"));
            Assert.AreEqual("true", DefaultJson.ToJson(true));
        }

        // ===== 已知系统类型（DateTime 等不再静默丢失） =====

        [Test]
        public void KnownTypes_RoundTrip()
        {
            var obj = new KnownTypesClass
            {
                dateTime = new DateTime(2026, 8, 15, 12, 34, 56, DateTimeKind.Utc),
                guid = Guid.NewGuid(),
                timeSpan = TimeSpan.FromHours(1.5),
                dateTimeOffset = new DateTimeOffset(2026, 8, 15, 8, 0, 0, TimeSpan.FromHours(8))
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<KnownTypesClass>(json);
            Assert.AreEqual(obj.dateTime, result.dateTime);
            Assert.AreEqual(obj.guid, result.guid);
            Assert.AreEqual(obj.timeSpan, result.timeSpan);
            Assert.AreEqual(obj.dateTimeOffset, result.dateTimeOffset);
        }

        // ===== 非有限浮点（NaN/Infinity 字面量往返） =====

        [Test]
        public void NaN_RoundTrip()
        {
            var obj = new AllPrimitivesClass { floatVal = float.NaN, doubleVal = double.NegativeInfinity };
            string json = DefaultJson.ToJson(obj, removeNulls: false);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.IsTrue(float.IsNaN(result.floatVal));
            Assert.IsTrue(double.IsNegativeInfinity(result.doubleVal));
        }

        // ===== 标准字典格式（对象格式输出 + legacy 格式兼容读取） =====

        [Test]
        public void Dictionary_StandardObjectFormat()
        {
            var dict = new Dictionary<string, int> { { "hp", 100 }, { "mp", 50 } };
            string json = DefaultJson.ToJson(dict);
            Assert.IsTrue(json.StartsWith("{"), "字典必须输出标准对象格式: " + json);
            Assert.IsFalse(json.Contains("\"key\""), "不得含 key/value 包装: " + json);
        }

        [Test]
        public void Dictionary_LegacyEntryArrayFormat_StillReadable()
        {
            // 旧版本存档格式：[{"key":..,"value":..}]
            var dict = DefaultJson.FromJson<Dictionary<string, int>>("[{\"key\":\"a\",\"value\":1},{\"key\":\"b\",\"value\":2}]");
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(1, dict["a"]);
            Assert.AreEqual(2, dict["b"]);
        }

        [Test]
        public void Dictionary_NonStringKeys_RoundTrip()
        {
            var obj = new CollectionClass
            {
                dictIS = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } }
            };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<CollectionClass>(json);
            Assert.AreEqual("one", result.dictIS[1]);
            Assert.AreEqual("two", result.dictIS[2]);
        }

        [Test]
        public void Dictionary_EscapedKeys_RoundTrip()
        {
            var dict = new Dictionary<string, int> { { "a\"b\nc", 7 } };
            string json = DefaultJson.ToJson(dict);
            var result = DefaultJson.FromJson<Dictionary<string, int>>(json);
            Assert.AreEqual(7, result["a\"b\nc"]);
        }

        // ===== 引用环（对齐 Newtonsoft ReferenceLoopHandling.Ignore：跳过成环成员，不抛错） =====

        [Test]
        public void ReferenceLoop_SelfReference_MemberSkipped()
        {
            var root = new SelfReferenceClass { id = "root" };
            root.child = root; // 自引用
            string json = DefaultJson.ToJson(root);
            Assert.AreEqual("{\"id\":\"root\"}", json, "自引用成员应被跳过而非抛错/递归: " + json);
        }

        [Test]
        public void ReferenceLoop_TwoNodeCycle_MembersSkipped()
        {
            var a = new SelfReferenceClass { id = "a" };
            var b = new SelfReferenceClass { id = "b" };
            a.child = b;
            b.child = a; // 双节点环
            string json = DefaultJson.ToJson(a);
            StringAssert.Contains("\"id\":\"a\"", json);
            StringAssert.Contains("\"id\":\"b\"", json);
            var back = DefaultJson.FromJson<SelfReferenceClass>(json);
            Assert.AreEqual("a", back.id);
            Assert.AreEqual("b", back.child.id);
            Assert.IsNull(back.child.child, "环截断后 child.child 应为 null");
        }

        [Test]
        public void ReferenceLoop_ListContainingItself_ElementSkipped()
        {
            var list = new List<object> { "x" };
            list.Add(list); // 列表包含自身
            string json = DefaultJson.ToJson(list);
            Assert.AreEqual("[\"x\"]", json, "自包含元素应被跳过: " + json);
        }

        [Test]
        public void ReferenceLoop_DictValuePointingToSelf_EntrySkipped()
        {
            var dict = new Dictionary<string, object> { { "a", 1 } };
            dict["self"] = dict; // 值指向自身
            string json = DefaultJson.ToJson(dict);
            Assert.AreEqual("{\"a\":1}", json, "成环键值应对被跳过: " + json);
        }

        [Test]
        public void ReferenceLoop_BytePath_SelfReference_MemberSkipped()
        {
            var root = new SelfReferenceClass { id = "root" };
            root.child = root;
            byte[] bytes = DefaultJson.ToJsonBytes(root);
            Assert.AreEqual("{\"id\":\"root\"}", System.Text.Encoding.UTF8.GetString(bytes));
        }

        [Test]
        public void ReferenceLoop_SameObjectTwiceAsSiblings_BothSerialized()
        {
            // DAG（非环）：同一对象在兄弟位置重复出现 → 两处都必须完整序列化（栈纪律验证）
            var shared = new SimpleClass { name = "shared", age = 1, active = true };
            var both = new List<object> { shared, shared };
            string json = DefaultJson.ToJson(both);
            int occurrences = CountOccurrences(json, "\"name\":\"shared\"");
            Assert.AreEqual(2, occurrences, "兄弟位置重复出现的同一对象不是环，两处都应序列化: " + json);
        }

        private static int CountOccurrences(string s, string sub)
        {
            int count = 0, index = 0;
            while ((index = s.IndexOf(sub, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += sub.Length;
            }

            return count;
        }

        /// <summary>自定义结构体：含只读计算属性（模拟 Vector3.normalized 假环场景）。</summary>
        [System.Serializable]
        private struct CustomPoint
        {
            public int X;
            public int Y;
            public string Label;

            public double Length => System.Math.Sqrt(X * X + Y * Y); // 只读计算属性
        }

        [Test]
        public void DeepNesting_BeyondLimit_SoftTruncated_NotThrow()
        {
            // 深链（80 层，超过默认 64 限制）：软截断（跳过超限成员+警告），不抛异常，输出保持合法
            // 链构造后根为 n79（最后创建），最深端为 n0
            SelfReferenceClass node = null;
            for (int i = 0; i < 80; i++)
            {
                node = new SelfReferenceClass { id = "n" + i, child = node };
            }

            string json = DefaultJson.ToJson(node);
            StringAssert.Contains("\"id\":\"n79\"", json, "根节点必须保留");
            Assert.IsFalse(json.Contains("\"id\":\"n0\""), "超过深度限制的最深节点应被截断");
            Assert.IsFalse(json.Contains("\"id\":null"), "边界对象的标量字段（如字符串 id）不得被安全网误写为 null");
            Assert.IsTrue(json.TrimEnd().EndsWith("}"), "软截断后输出必须是合法 JSON");

            // 截断后的 JSON 仍可完整反序列化
            var back = DefaultJson.FromJson<SelfReferenceClass>(json);
            Assert.AreEqual("n79", back.id);
        }

        // ===== 精度与转义 =====

        [Test]
        public void Float_0_1_ExactRoundTrip()
        {
            var obj = new AllPrimitivesClass { floatVal = 0.1f };
            string json = DefaultJson.ToJson(obj);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(json);
            Assert.IsTrue(0.1f.Equals(result.floatVal), "0.1f 必须精确往返: " + json);
        }

        [Test]
        public void UnicodeEscape_Parses()
        {
            Assert.AreEqual("你好", DefaultJson.FromJson<string>("\"\\u4F60\\u597D\""));
        }

        [Test]
        public void Nullable_NullLiteral_RoundTrip()
        {
            var obj = new NullableClass { nullableInt = null, nullableBool = null };
            string json = DefaultJson.ToJson(obj, removeNulls: false);
            var result = DefaultJson.FromJson<NullableClass>(json);
            Assert.IsNull(result.nullableInt);
            Assert.IsNull(result.nullableBool);
        }

        [Test]
        public void Nullable_Value_RoundTrip()
        {
            var obj = new NullableClass { nullableInt = 42, nullableBool = true };
            string json = DefaultJson.ToJson(obj, removeNulls: false);
            var result = DefaultJson.FromJson<NullableClass>(json);
            Assert.AreEqual(42, result.nullableInt);
            Assert.AreEqual(true, result.nullableBool);
        }

        [Test]
        public void QuotedNumbers_HistoricalFormat_StillReadable()
        {
            // 历史带引号数值（旧版本序列化产物）
            var obj = DefaultJson.FromJson<AllPrimitivesClass>("{\"floatVal\":\"1.5\",\"doubleVal\":\"2.25\"}");
            Assert.AreEqual(1.5f, obj.floatVal);
            Assert.AreEqual(2.25d, obj.doubleVal);
        }

        // ===== 整数溢出防护（先乘后查回绕漏洞回归） =====

        [Test]
        public void IntegerOverflow_ULongMaxPlusOne_Rejected()
        {
            // 18446744073709551616 = ulong.MaxValue + 1：无符号回绕会变成 0——必须报错而非静默接受
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<ulong>("18446744073709551616"));
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<ulong>(System.Text.Encoding.UTF8.GetBytes("18446744073709551616")));
        }

        [Test]
        public void IntegerOverflow_TwentyDigits_Rejected()
        {
            // 99999999999999999999（20 位）：远超 ulong，回绕后可能落回范围内——必须报错
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<long>("99999999999999999999"));
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<long>(System.Text.Encoding.UTF8.GetBytes("99999999999999999999")));
        }

        [Test]
        public void IntegerOverflow_LongBoundary_ExactValuesAccepted()
        {
            // 边界值本身必须精确通过（修复不得误伤）
            Assert.AreEqual(long.MaxValue, DefaultJson.FromJson<long>("9223372036854775807"));
            Assert.AreEqual(long.MinValue, DefaultJson.FromJson<long>("-9223372036854775808"));
            Assert.AreEqual(ulong.MaxValue, DefaultJson.FromJson<ulong>("18446744073709551615"));

            Assert.AreEqual(long.MaxValue, DefaultJson.FromJson<long>(System.Text.Encoding.UTF8.GetBytes("9223372036854775807")));
            Assert.AreEqual(long.MinValue, DefaultJson.FromJson<long>(System.Text.Encoding.UTF8.GetBytes("-9223372036854775808")));
            Assert.AreEqual(ulong.MaxValue, DefaultJson.FromJson<ulong>(System.Text.Encoding.UTF8.GetBytes("18446744073709551615")));

            // 边界 +1 必须拒绝
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<long>("9223372036854775808"));
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<long>(System.Text.Encoding.UTF8.GetBytes("9223372036854775808")));
        }

        // ===== FromJsonOverwrite 对 legacy 字典格式的支持 =====

        [Test]
        public void Overwrite_Dictionary_LegacyArrayFormat()
        {
            // 历史存档为 legacy 条目数组格式：覆盖模式必须按 token 分发而非假定 '{'
            var dict = new Dictionary<string, int> { { "stale", -1 } };
            DefaultJson.FromJsonOverwrite(dict, "[{\"key\":\"a\",\"value\":1},{\"key\":\"b\",\"value\":2}]");
            Assert.AreEqual(2, dict.Count, "覆盖后旧键应被清空");
            Assert.AreEqual(1, dict["a"]);
            Assert.AreEqual(2, dict["b"]);
        }

        [Test]
        public void Overwrite_Dictionary_StandardFormat_StillWorks()
        {
            var dict = new Dictionary<string, int> { { "stale", -1 } };
            DefaultJson.FromJsonOverwrite(dict, "{\"a\":1,\"b\":2}");
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(1, dict["a"]);
            Assert.AreEqual(2, dict["b"]);
        }

        [Test]
        public void Overwrite_Dictionary_LegacyFormat_BytePath()
        {
            var dict = new Dictionary<string, int> { { "stale", -1 } };
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("[{\"key\":\"a\",\"value\":1}]");
            // 字节覆盖路径（经 ByteReader.Parse 入口，InternalsVisibleTo 可见）
            var reader = new Moirai.Atropos.DefaultJson.ByteReader(bytes, 64);
            reader.Parse(typeof(Dictionary<string, int>), dict);
            Assert.AreEqual(1, dict.Count);
            Assert.AreEqual(1, dict["a"]);
        }

        [Test]
        public void Error_Contains_Position()
        {
            try
            {
                DefaultJson.FromJson<SimpleClass>("{\r\n  \"name\": \"a\",\r\n  ?bad");
                Assert.Fail("应当抛出异常");
            }
            catch (Moirai.Atropos.GameException e)
            {
                Assert.IsTrue(e.Message.Contains("line"), "错误信息应包含行号: " + e.Message);
            }
        }

        // ===== 属性序列化契约（对齐原实现：默认读写兼备，往返对称） =====

        [System.Serializable]
        private class ReadOnlyPropsClass
        {
            public int width = 10;
            public int height = 5;

            // 只读计算属性：按契约排除（无 setter 不可往返，且计算属性每次求值可能产生新副本）
            public int Area => width * height;

            // 读写属性：正常序列化
            public string Label { get; set; }
        }

        [Test]
        public void PropertyContract_ReadOnlyExcluded_WritableIncluded()
        {
            var obj = new ReadOnlyPropsClass { Label = "rect" };
            string json = DefaultJson.ToJson(obj);
            Assert.IsFalse(json.Contains("\"Area\""), "get-only 计算属性不得序列化: " + json);
            Assert.IsTrue(json.Contains("\"Label\":\"rect\""), "读写属性必须序列化: " + json);
        }

        [Test]
        public void PropertyContract_AnonymousType_NoSerializableMembers_Throws()
        {
            // 匿名类型成员均为 get-only：按往返对称契约不可序列化（原实现行为一致），
            // 且无任何可序列化字段 → 显式失败而非静默 "{}"
            var anon = new { name = "temp", value = 42 };
            Assert.Throws<Moirai.Atropos.GameException>(() => DefaultJson.ToJson(anon));
        }

        #endregion

        #region 字节通路测试 [BYTE PATH]

        [Test]
        public void Bytes_EquivalentToEncodedString()
        {
            var obj = new CollectionClass
            {
                intList = new List<int> { 1, 2, 3 },
                dictSI = new Dictionary<string, int> { { "a", 1 } },
                stringArray = new[] { "x", "y" }
            };
            string jsonStr = DefaultJson.ToJson(obj);
            byte[] jsonBytes = DefaultJson.ToJsonBytes(obj);
            Assert.AreEqual(jsonStr, System.Text.Encoding.UTF8.GetString(jsonBytes), "字节输出必须与 string 输出 UTF8 编码逐字节等价");
        }

        [Test]
        public void Bytes_AllPrimitives_RoundTrip()
        {
            var obj = new AllPrimitivesClass
            {
                boolVal = true, byteVal = 255, sbyteVal = -128, shortVal = 32767, ushortVal = 65535,
                intVal = -42, uintVal = 4294967295, longVal = long.MinValue, ulongVal = ulong.MaxValue,
                floatVal = 0.1f, doubleVal = 2.718281828459045, decimalVal = 123.456m, charVal = 'Z',
                stringVal = "test"
            };
            byte[] bytes = DefaultJson.ToJsonBytes(obj, removeNulls: false);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(bytes);
            Assert.AreEqual(true, result.boolVal);
            Assert.AreEqual(255, result.byteVal);
            Assert.AreEqual(-128, result.sbyteVal);
            Assert.AreEqual(32767, result.shortVal);
            Assert.AreEqual(65535, result.ushortVal);
            Assert.AreEqual(-42, result.intVal);
            Assert.AreEqual(4294967295, result.uintVal);
            Assert.AreEqual(long.MinValue, result.longVal);
            Assert.AreEqual(ulong.MaxValue, result.ulongVal);
            Assert.IsTrue(0.1f.Equals(result.floatVal), "float 必须精确往返");
            Assert.AreEqual(2.718281828459045, result.doubleVal);
            Assert.AreEqual(123.456m, result.decimalVal);
            Assert.AreEqual('Z', result.charVal);
            Assert.AreEqual("test", result.stringVal);
        }

        [Test]
        public void Bytes_CJK_And_Emoji_RoundTrip()
        {
            var obj = new SimpleClass { name = "你好世界🌍🚀中文", age = 1, active = true };
            byte[] bytes = DefaultJson.ToJsonBytes(obj);
            var result = DefaultJson.FromJson<SimpleClass>(bytes);
            Assert.AreEqual("你好世界🌍🚀中文", result.name, "多字节 UTF8（含 4 字节代理对 emoji）必须精确往返");
        }

        [Test]
        public void Bytes_EscapedUnicode_InInput_Parses()
        {
            // 含 \uXXXX 转义的输入（小写十六进制）必须正确反转义
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("\"\\u4f60\\u597d\"");
            Assert.AreEqual("你好", DefaultJson.FromJson<string>(bytes));
        }

        [Test]
        public void Bytes_BOM_Skipped()
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"ok\",\"age\":1,\"active\":true}");
            byte[] withBom = new byte[3 + payload.Length];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            System.Array.Copy(payload, 0, withBom, 3, payload.Length);
            var result = DefaultJson.FromJson<SimpleClass>(withBom);
            Assert.AreEqual("ok", result.name);
        }

        [Test]
        public void Bytes_TypedArrays_RoundTrip()
        {
            var obj = new CollectionClass
            {
                intArray = new[] { int.MinValue, -1, 0, 1, int.MaxValue },
                stringArray = new[] { "a", "你好", null, "" }
            };
            byte[] bytes = DefaultJson.ToJsonBytes(obj, removeNulls: false);
            var result = DefaultJson.FromJson<CollectionClass>(bytes);
            CollectionAssert.AreEqual(new[] { int.MinValue, -1, 0, 1, int.MaxValue }, result.intArray);
            CollectionAssert.AreEqual(new[] { "a", "你好", null, "" }, result.stringArray);
        }

        [Test]
        public void Bytes_TypedFloatArray_RoundTrip()
        {
            float[] floats = { 0.1f, -3.25f, float.MaxValue, float.MinValue, 1e-30f };
            byte[] bytes = DefaultJson.ToJsonBytes(floats);
            var result = DefaultJson.FromJson<float[]>(bytes);
            CollectionAssert.AreEqual(floats, result, "float 数组必须精确往返");
        }

        [Test]
        public void Bytes_TypedLongDoubleBoolArrays_RoundTrip()
        {
            byte[] b1 = DefaultJson.ToJsonBytes(new long[] { long.MinValue, 0, long.MaxValue });
            CollectionAssert.AreEqual(new long[] { long.MinValue, 0, long.MaxValue }, DefaultJson.FromJson<long[]>(b1));

            byte[] b2 = DefaultJson.ToJsonBytes(new double[] { 0.1d, -2.5e-10d, 1e300d });
            CollectionAssert.AreEqual(new double[] { 0.1d, -2.5e-10d, 1e300d }, DefaultJson.FromJson<double[]>(b2));

            byte[] b3 = DefaultJson.ToJsonBytes(new bool[] { true, false, true });
            CollectionAssert.AreEqual(new bool[] { true, false, true }, DefaultJson.FromJson<bool[]>(b3));
        }

        [Test]
        public void Bytes_ScientificNotationIntArray_Parses()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("[1e2,2e1,3]");
            CollectionAssert.AreEqual(new[] { 100, 20, 3 }, DefaultJson.FromJson<int[]>(bytes), "科学计数法整值必须兼容");
        }

        [Test]
        public void Bytes_QuotedNumbersArray_HistoricalFormat_Parses()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("[\"1\",\"2\",\"3\"]");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, DefaultJson.FromJson<int[]>(bytes), "历史带引号数值数组必须兼容");
        }

        [Test]
        public void Bytes_NestedGraph_RoundTrip()
        {
            var obj = new CollectionClass
            {
                intList = new List<int> { 5, 6, 7 },
                dictSObj = new Dictionary<string, SimpleClass>
                {
                    { "first", new SimpleClass { name = "Alice", age = 30, active = true } }
                }
            };
            byte[] bytes = DefaultJson.ToJsonBytes(obj);
            var result = DefaultJson.FromJson<CollectionClass>(bytes);
            CollectionAssert.AreEqual(new[] { 5, 6, 7 }, result.intList);
            Assert.AreEqual("Alice", result.dictSObj["first"].name);
            Assert.AreEqual(30, result.dictSObj["first"].age);
        }

        [Test]
        public void Bytes_LegacyDictFormat_StillReadable()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("[{\"key\":\"a\",\"value\":1},{\"key\":\"b\",\"value\":2}]");
            var dict = DefaultJson.FromJson<Dictionary<string, int>>(bytes);
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual(1, dict["a"]);
        }

        [Test]
        public void Bytes_Truncated_Throws()
        {
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<List<int>>(System.Text.Encoding.UTF8.GetBytes("[1,2,3")));
            Assert.Throws<Moirai.Atropos.GameException>(() =>
                DefaultJson.FromJson<SimpleClass>(System.Text.Encoding.UTF8.GetBytes("{\"name\":\"a\"")));
        }

        [Test]
        public void Bytes_UnknownFields_Ignored()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"ok\",\"unknownField\":2,\"another\":{\"deep\":[1,2]}}");
            var obj = DefaultJson.FromJson<SimpleClass>(bytes);
            Assert.AreEqual("ok", obj.name);
            Assert.AreEqual(0, obj.age);
        }

        [Test]
        public void Bytes_NullLiteral_Root()
        {
            Assert.IsNull(DefaultJson.FromJson<SimpleClass>(System.Text.Encoding.UTF8.GetBytes("null")));
        }

        [Test]
        public void Bytes_NaN_RoundTrip()
        {
            var obj = new AllPrimitivesClass { floatVal = float.NaN, doubleVal = double.PositiveInfinity };
            byte[] bytes = DefaultJson.ToJsonBytes(obj, removeNulls: false);
            var result = DefaultJson.FromJson<AllPrimitivesClass>(bytes);
            Assert.IsTrue(float.IsNaN(result.floatVal));
            Assert.IsTrue(double.IsPositiveInfinity(result.doubleVal));
        }

        [Test]
        public void Bytes_Culture_DeDE_RoundTrip()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var obj = new AllPrimitivesClass { decimalVal = 1234.56m, floatVal = 1.5f };
                byte[] bytes = DefaultJson.ToJsonBytes(obj);
                var result = DefaultJson.FromJson<AllPrimitivesClass>(bytes);
                Assert.AreEqual(1234.56m, result.decimalVal);
                Assert.AreEqual(1.5f, result.floatVal);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void Facade_Bytes_RoundTrip()
        {
            // 门面字节 API：DefaultJsonHandler 实现了 IBufferJsonHandler（当前默认 handler 即为此）
            var payload = new Dictionary<string, int> { { "gold", 100 }, { "level", 3 } };
            byte[] bytes = Moirai.Atropos.JsonUtility.ToJsonBytes(payload);
            var back = Moirai.Atropos.JsonUtility.ToObject<Dictionary<string, int>>(bytes);
            Assert.AreEqual(100, back["gold"]);
            Assert.AreEqual(3, back["level"]);
        }

        // ===== 值类型结构体与计算属性（Vector3.normalized 假环场景） =====

        [Test]
        public void ValueTypeStruct_Vector3_RoundTrip()
        {
            var v = new UnityEngine.Vector3(1.5f, -2.25f, 3.75f);
            string json = DefaultJson.ToJson(v);
            Assert.AreEqual("{\"x\":1.5,\"y\":-2.25,\"z\":3.75}", json, "结构体按对象序列化且不含计算属性: " + json);
            var back = DefaultJson.FromJson<UnityEngine.Vector3>(json);
            Assert.AreEqual(1.5f, back.x);
            Assert.AreEqual(-2.25f, back.y);
            Assert.AreEqual(3.75f, back.z);
        }

        [Test]
        public void ValueTypeStruct_ComputedProperty_NotSerialized()
        {
            // Vector3.normalized 是只读计算属性：每次 get 返回新副本，若被序列化将产生无限嵌套链
            var v = new UnityEngine.Vector3(1f, 2f, 3f);
            string json = DefaultJson.ToJson(v);
            Assert.IsFalse(json.Contains("normalized"), "值类型上的只读计算属性不得序列化: " + json);
            Assert.IsFalse(json.Contains("magnitude"), "派生标量属性不得序列化: " + json);
        }

        [System.Serializable]
        private class HolderWithStruct
        {
            public UnityEngine.Vector3 offset;
            public string name;
        }

        [Test]
        public void ValueTypeStruct_NestedInClass_RoundTrip()
        {
            var obj = new HolderWithStruct { offset = new UnityEngine.Vector3(0.5f, 1f, -0.5f), name = "h" };
            string json = DefaultJson.ToJson(obj);
            Assert.IsFalse(json.Contains("normalized"), "嵌套结构体的计算属性不得序列化: " + json);
            var back = DefaultJson.FromJson<HolderWithStruct>(json);
            Assert.AreEqual(0.5f, back.offset.x);
            Assert.AreEqual(1f, back.offset.y);
            Assert.AreEqual(-0.5f, back.offset.z);
            Assert.AreEqual("h", back.name);
        }

        [Test]
        public void ValueTypeStruct_HistoricalDeepGarbage_Recovers()
        {
            // 历史数据中的 normalized 无限链（软截断存储产物）：解析时跳过 normalized（未知字段），
            // x/y/z 正常还原，深度超限值软跳过不抛错
            var sb = new System.Text.StringBuilder("{\"x\":0,\"y\":0,\"z\":0");
            for (int i = 0; i < 100; i++) sb.Append(",\"normalized\":{\"x\":1,\"y\":2,\"z\":3");
            sb.Append('}', 101);
            var v = DefaultJson.FromJson<UnityEngine.Vector3>(sb.ToString());
            Assert.AreEqual(0f, v.x);
            Assert.AreEqual(0f, v.y);
            Assert.AreEqual(0f, v.z);
        }

        [Test]
        public void ValueTypeStruct_BytePath_Vector3_RoundTrip()
        {
            var v = new UnityEngine.Vector3(4.5f, 5.5f, 6.5f);
            byte[] bytes = DefaultJson.ToJsonBytes(v);
            var back = DefaultJson.FromJson<UnityEngine.Vector3>(bytes);
            Assert.AreEqual(4.5f, back.x);
            Assert.AreEqual(5.5f, back.y);
            Assert.AreEqual(6.5f, back.z);
        }

        [Test]
        public void CustomStruct_ObjectParse_RoundTrip()
        {
            var s = new CustomPoint { X = 7, Y = -8, Label = "pt" };
            string json = DefaultJson.ToJson(s);
            var back = DefaultJson.FromJson<CustomPoint>(json);
            Assert.AreEqual(7, back.X);
            Assert.AreEqual(-8, back.Y);
            Assert.AreEqual("pt", back.Label);
        }

        [Test]
        public void CustomStruct_ComputedProperty_NotSerialized()
        {
            var s = new CustomPoint { X = 3, Y = 4, Label = "pt" };
            string json = DefaultJson.ToJson(s);
            Assert.IsFalse(json.Contains("Length"), "自定义结构体的只读计算属性不得序列化: " + json);
        }

        #endregion
    }
}
