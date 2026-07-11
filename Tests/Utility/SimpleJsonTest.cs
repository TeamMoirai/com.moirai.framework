using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    public class SimpleJsonTest
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual(true, result.active);
        }

        [Test]
        public void Bool_False_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 1, active = false };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual(false, result.active);
        }

        [Test]
        public void Int_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 42, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual(42, result.age);
        }

        [Test]
        public void Int_Negative_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = -100, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual(-100, result.age);
        }

        [Test]
        public void Int_Zero_RoundTrip()
        {
            var obj = new SimpleClass { name = "a", age = 0, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual(0, result.age);
        }

        [Test]
        public void String_RoundTrip()
        {
            var obj = new SimpleClass { name = "hello world", age = 1, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual("hello world", result.name);
        }

        [Test]
        public void String_Empty_RoundTrip()
        {
            var obj = new SimpleClass { name = "", age = 1, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual("", result.name);
        }

        [Test]
        public void String_WithSpecialChars_RoundTrip()
        {
            var obj = new SimpleClass { name = "a\"b\\c\nd\te", age = 1, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
            Assert.AreEqual("a\"b\\c\nd\te", result.name);
        }

        [Test]
        public void String_Unicode_RoundTrip()
        {
            var obj = new SimpleClass { name = "你好世界🌍", age = 1, active = true };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<SimpleClass>(json);
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

            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);

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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);
            Assert.AreEqual(1.5f, result.floatVal, 0.001f);
        }

        [Test]
        public void Double_RoundTrip()
        {
            var obj = new AllPrimitivesClass { doubleVal = 123.456789 };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);
            Assert.AreEqual(123.456789, result.doubleVal, 0.000001);
        }

        [Test]
        public void Decimal_RoundTrip()
        {
            var obj = new AllPrimitivesClass { decimalVal = 99.99m };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);
            Assert.AreEqual(99.99m, result.decimalVal);
        }

        [Test]
        public void Long_MaxValue_RoundTrip()
        {
            var obj = new AllPrimitivesClass { longVal = long.MaxValue };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);
            Assert.AreEqual(long.MaxValue, result.longVal);
        }

        [Test]
        public void Long_MinValue_RoundTrip()
        {
            var obj = new AllPrimitivesClass { longVal = long.MinValue };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<AllPrimitivesClass>(json);
            Assert.AreEqual(long.MinValue, result.longVal);
        }

        #endregion

        #region 枚举类型测试 [ENUM TYPES]

        [Test]
        public void Enum_RoundTrip()
        {
            var obj = new EnumClass { enumVal = TestEnum.Second, enumVal2 = TestEnum.None };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<EnumClass>(json);
            Assert.AreEqual(TestEnum.Second, result.enumVal);
            Assert.AreEqual(TestEnum.None, result.enumVal2);
        }

        [Test]
        public void Enum_FirstValue_RoundTrip()
        {
            var obj = new EnumClass { enumVal = TestEnum.First };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<EnumClass>(json);
            Assert.AreEqual(TestEnum.First, result.enumVal);
        }

        #endregion

        #region 集合类型测试 [COLLECTION TYPES]

        [Test]
        public void List_Int_RoundTrip()
        {
            var obj = new CollectionClass { intList = new List<int> { 1, 2, 3, 4, 5 } };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
            Assert.IsNotNull(result.intList);
            Assert.AreEqual(5, result.intList.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.intList);
        }

        [Test]
        public void List_String_RoundTrip()
        {
            var obj = new CollectionClass { stringList = new List<string> { "a", "b", "c" } };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
            Assert.IsNotNull(result.stringList);
            Assert.AreEqual(3, result.stringList.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result.stringList);
        }

        [Test]
        public void List_Empty_RoundTrip()
        {
            var obj = new CollectionClass { intList = new List<int>() };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
            Assert.IsNotNull(result.intList);
            Assert.AreEqual(0, result.intList.Count);
        }

        [Test]
        public void Array_Int_RoundTrip()
        {
            var obj = new CollectionClass { intArray = new int[] { 10, 20, 30 } };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
            Assert.IsNotNull(result.intArray);
            Assert.AreEqual(3, result.intArray.Length);
            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, result.intArray);
        }

        [Test]
        public void Array_String_RoundTrip()
        {
            var obj = new CollectionClass { stringArray = new string[] { "x", "y", "z" } };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
            Assert.IsNotNull(result.stringArray);
            Assert.AreEqual(3, result.stringArray.Length);
            CollectionAssert.AreEqual(new[] { "x", "y", "z" }, result.stringArray);
        }

        [Test]
        public void Array_Empty_RoundTrip()
        {
            var obj = new CollectionClass { intArray = new int[0] };
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<CollectionClass>(json);
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
            string json = SimpleJson.ToJSON(obj);
            var result = SimpleJson.FromJSON<NestedClass>(json);
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
            string json = SimpleJson.ToJSON(list);
            var result = SimpleJson.FromJSON<List<SimpleClass>>(json);
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
            string json = SimpleJson.ToJSON(arr);
            var result = SimpleJson.FromJSON<SimpleClass[]>(json);
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
            string json = SimpleJson.ToJSON(obj, removeNulls: true);
            Assert.IsFalse(json.Contains("name"));
            Assert.IsTrue(json.Contains("5"));
        }

        [Test]
        public void Null_String_IncludedWhenNotRemoveNulls()
        {
            var obj = new SimpleClass { name = null, age = 5, active = true };
            string json = SimpleJson.ToJSON(obj, removeNulls: false);
            Assert.IsTrue(json.Contains("name"));
            Assert.IsTrue(json.Contains("null"));
        }

        [Test]
        public void Null_Object_RoundTrip()
        {
            var obj = new NestedClass { label = "test", child = null };
            string json = SimpleJson.ToJSON(obj, removeNulls: false);
            var result = SimpleJson.FromJSON<NestedClass>(json);
            Assert.AreEqual("test", result.label);
        }

        #endregion

        #region FromJSONOverwrite 测试 [OVERWRITE]

        [Test]
        public void Overwrite_SingleField()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            SimpleJson.FromJSONOverwrite(obj, "{\"name\":\"new\"}");
            Assert.AreEqual("new", obj.name);
            Assert.AreEqual(1, obj.age);
            Assert.AreEqual(false, obj.active);
        }

        [Test]
        public void Overwrite_AllFields()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            SimpleJson.FromJSONOverwrite(obj, "{\"name\":\"new\",\"age\":99,\"active\":true}");
            Assert.AreEqual("new", obj.name);
            Assert.AreEqual(99, obj.age);
            Assert.AreEqual(true, obj.active);
        }

        [Test]
        public void Overwrite_PartialFields()
        {
            var obj = new SimpleClass { name = "old", age = 1, active = false };
            SimpleJson.FromJSONOverwrite(obj, "{\"age\":42}");
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
            string json = SimpleJson.ToJSON(obj, readable: true);
            Assert.IsTrue(json.Contains("\r\n"));
            Assert.IsTrue(json.Contains("\t"));
        }

        [Test]
        public void ToJSON_Compact_NoWhitespace()
        {
            var obj = new SimpleClass { name = "test", age = 5, active = true };
            string json = SimpleJson.ToJSON(obj, readable: false);
            Assert.IsFalse(json.Contains("\r\n"));
            Assert.IsFalse(json.Contains("\t"));
        }

        #endregion

        #region MemoryPool 复用测试 [POOL REUSE]

        [Test]
        public void Pool_Reuses_DeserializationObject()
        {
            var obj = new SimpleClass { name = "a", age = 1, active = true };
            string json = SimpleJson.ToJSON(obj);

            var r1 = SimpleJson.FromJSON<SimpleClass>(json);
            var r2 = SimpleJson.FromJSON<SimpleClass>(json);

            Assert.AreEqual("a", r1.name);
            Assert.AreEqual("a", r2.name);
        }

        [Test]
        public void Pool_Reuses_SerializationObject()
        {
            var obj1 = new SimpleClass { name = "a", age = 1, active = true };
            var obj2 = new SimpleClass { name = "b", age = 2, active = false };

            string json1 = SimpleJson.ToJSON(obj1);
            string json2 = SimpleJson.ToJSON(obj2);

            Assert.IsTrue(json1.Contains("a"));
            Assert.IsTrue(json2.Contains("b"));
            Assert.IsFalse(json2.Contains("a"));
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

            string json1 = SimpleJson.ToJSON(simple);
            string json2 = SimpleJson.ToJSON(nested);
            string json3 = SimpleJson.ToJSON(collection);

            var r1 = SimpleJson.FromJSON<SimpleClass>(json1);
            var r2 = SimpleJson.FromJSON<NestedClass>(json2);
            var r3 = SimpleJson.FromJSON<CollectionClass>(json3);

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
            string json = SimpleJson.ToJSON(obj);
            object result = SimpleJson.FromJSON(json, typeof(SimpleClass));
            Assert.IsNotNull(result);
            Assert.IsInstanceOf<SimpleClass>(result);
            Assert.AreEqual("test", ((SimpleClass)result).name);
            Assert.AreEqual(42, ((SimpleClass)result).age);
        }

        #endregion
    }
}
