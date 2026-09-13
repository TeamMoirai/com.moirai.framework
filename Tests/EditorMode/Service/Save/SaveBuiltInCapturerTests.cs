using System;
using System.Collections.Generic;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Service.Save
{
    /// <summary>
    /// 内置捕获器测试：Transform 三字段往返、Rigidbody 速度往返与运动学跳过、ParticleSystem 时间往返、注册表登记。
    /// </summary>
    public class SaveBuiltInCapturerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            SaveBuiltInCapturers.RegisterBuiltIns();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
        }

        /// <summary>
        /// 创建测试物体（登记清理清单）。
        /// </summary>
        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject("bic-test-" + name);
            _objects.Add(gameObject);
            return gameObject;
        }

        /// <summary>
        /// 捕获 → 恢复往返（全字段掩码）。
        /// </summary>
        private static void RoundTrip(ISaveComponentCapturer capturer, Component source, Component target)
        {
            var mask = new SaveFieldMask(capturer.FieldNames, new HashSet<string>(capturer.FieldNames));
            var writer = new SaveKeyValueWriter(128);
            capturer.Capture(source, ref writer, mask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType type));
            Assert.AreEqual(ESaveKvType.Object, type);
            int recordCount = reader.ReadChildCount();
            capturer.Restore(target, ref reader, recordCount, mask);
        }

        [Test]
        public void RegisterBuiltIns_RegistersEngineCapturers()
        {
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Transform), out _));
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Rigidbody), out _));
            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(ParticleSystem), out _));
        }

        [Test]
        public void TransformCapturer_RoundTripsLocalTRS()
        {
            GameObject source = CreateObject("src");
            source.transform.localPosition = new Vector3(1f, 2f, 3f);
            source.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            source.transform.localScale = new Vector3(2f, 3f, 4f);
            GameObject target = CreateObject("dst");

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Transform), out ISaveComponentCapturer capturer));
            RoundTrip(capturer, source.transform, target.transform);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.transform.localPosition);
            Assert.AreEqual(20f, target.transform.localEulerAngles.y, 0.01f);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), target.transform.localScale);
        }

        [Test]
        public void RigidbodyCapturer_RoundTripsVelocities()
        {
            GameObject source = CreateObject("src");
            var sourceBody = source.AddComponent<Rigidbody>();
            sourceBody.linearVelocity = new Vector3(1f, 0f, 2f);
            sourceBody.angularVelocity = new Vector3(0f, 1f, 0f);
            GameObject target = CreateObject("dst");
            var targetBody = target.AddComponent<Rigidbody>();

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Rigidbody), out ISaveComponentCapturer capturer));
            RoundTrip(capturer, sourceBody, targetBody);

            Assert.AreEqual(new Vector3(1f, 0f, 2f), targetBody.linearVelocity);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), targetBody.angularVelocity);
        }

        [Test]
        public void RigidbodyCapturer_KinematicTarget_SkipsVelocityRestore()
        {
            GameObject source = CreateObject("src");
            var sourceBody = source.AddComponent<Rigidbody>();
            sourceBody.linearVelocity = new Vector3(1f, 0f, 2f);
            GameObject target = CreateObject("dst");
            var targetBody = target.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Rigidbody), out ISaveComponentCapturer capturer));
            RoundTrip(capturer, sourceBody, targetBody);

            Assert.AreEqual(Vector3.zero, targetBody.linearVelocity, "运动学刚体跳过速度写回");
        }

        [Test]
        public void ParticleSystemCapturer_RoundTripsTime()
        {
            GameObject source = CreateObject("src");
            var sourceParticles = source.AddComponent<ParticleSystem>();
            sourceParticles.time = 2.5f;
            GameObject target = CreateObject("dst");
            var targetParticles = target.AddComponent<ParticleSystem>();

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(ParticleSystem), out ISaveComponentCapturer capturer));
            RoundTrip(capturer, sourceParticles, targetParticles);

            Assert.AreEqual(2.5f, targetParticles.time, 0.0001f);
        }

        [Test]
        public void TransformCapturer_Mask_DisabledFieldKept()
        {
            GameObject source = CreateObject("src");
            source.transform.localPosition = new Vector3(9f, 9f, 9f);
            source.transform.localScale = new Vector3(2f, 2f, 2f);
            GameObject target = CreateObject("dst");

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(Transform), out ISaveComponentCapturer capturer));
            // 仅勾选 localScale——localPosition 不捕获不恢复
            var mask = new SaveFieldMask(capturer.FieldNames, new HashSet<string> { TransformCapturer.LocalScaleKey });
            var writer = new SaveKeyValueWriter(128);
            capturer.Capture(source.transform, ref writer, mask);
            var reader = new SaveKeyValueReader(writer.ToArray());
            Assert.IsTrue(reader.ReadRecord(out _, out _));
            int recordCount = reader.ReadChildCount();
            Assert.AreEqual(1, recordCount, "掩码禁用字段不写入");
            capturer.Restore(target.transform, ref reader, recordCount, mask);

            Assert.AreEqual(Vector3.zero, target.transform.localPosition, "禁用字段保持目标当前值");
            Assert.AreEqual(new Vector3(2f, 2f, 2f), target.transform.localScale);
        }
    }
}
