using System;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 内置捕获器注册入口：引擎组件（Transform/Rigidbody/ParticleSystem）的手写捕获器登记。
    /// <para>运行期经 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 自注册；编辑器期（Inspector 字段清单）
    /// 由编辑器程序集经同一 <see cref="RegisterBuiltIns"/> 入口注册（幂等）。</para>
    /// </summary>
    public static class SaveBuiltInCapturers
    {
        /// <summary>UTF-8 解码器（无 BOM；恢复侧键匹配用）。</summary>
        private static readonly Encoding s_Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 恢复侧键匹配（生成代码用预编码字节数组，内置捕获器字段数少，解码字符串比较更直观）。
        /// </summary>
        /// <param name="key">记录键（UTF-8 字节跨度）。</param>
        /// <param name="expected">期望键名。</param>
        /// <returns>匹配返回 <c>true</c>。</returns>
        internal static bool KeyEquals(ReadOnlySpan<byte> key, string expected)
        {
            return string.Equals(s_Utf8.GetString(key), expected, StringComparison.Ordinal);
        }

        /// <summary>
        /// 注册全部内置捕获器（幂等——同类型重复注册以最新为准）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterBuiltIns()
        {
            SaveCapturerRegistry.Register(typeof(Transform), new TransformCapturer());
            SaveCapturerRegistry.Register(typeof(Rigidbody), new RigidbodyCapturer());
            SaveCapturerRegistry.Register(typeof(ParticleSystem), new ParticleSystemCapturer());
        }
    }

    /// <summary>
    /// Transform 内置捕获器：localPosition/localRotation/localScale 三字段（局部空间——实体父子接线后局部坐标天然正确）。
    /// </summary>
    public sealed class TransformCapturer : ISaveComponentCapturer
    {
        /// <summary>字段键：局部位置。</summary>
        public const string LocalPositionKey = "localPosition";

        /// <summary>字段键：局部旋转。</summary>
        public const string LocalRotationKey = "localRotation";

        /// <summary>字段键：局部缩放。</summary>
        public const string LocalScaleKey = "localScale";

        /// <summary>全量字段名数组（索引 = 掩码索引）。</summary>
        private static readonly string[] s_FieldNames = { LocalPositionKey, LocalRotationKey, LocalScaleKey };

        /// <inheritdoc />
        public Type ComponentType => typeof(Transform);

        /// <inheritdoc />
        public string[] FieldNames => s_FieldNames;

        /// <inheritdoc />
        public int SchemaVersion => 1;

        /// <inheritdoc />
        public void Capture(object component, ref SaveKeyValueWriter writer, in SaveFieldMask mask)
        {
            var self = (Transform)component;
            int enabledCount = 0;
            for (int i = 0; i < s_FieldNames.Length; i++)
            {
                if (mask.IsEnabled(i))
                {
                    enabledCount++;
                }
            }

            writer.BeginNestedObject(ComponentType.FullName, enabledCount);
            if (mask.IsEnabled(0))
            {
                writer.WriteVector3(LocalPositionKey, self.localPosition);
            }

            if (mask.IsEnabled(1))
            {
                writer.WriteQuaternion(LocalRotationKey, self.localRotation);
            }

            if (mask.IsEnabled(2))
            {
                writer.WriteVector3(LocalScaleKey, self.localScale);
            }

            writer.EndNested();
        }

        /// <inheritdoc />
        public void Restore(object component, ref SaveKeyValueReader reader, int recordCount, in SaveFieldMask mask)
        {
            var self = (Transform)component;
            for (int consumed = 0; consumed < recordCount && reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type); consumed++)
            {
                if (SaveBuiltInCapturers.KeyEquals(key, LocalPositionKey) && mask.IsEnabled(0) && type == ESaveKvType.Vector3)
                {
                    self.localPosition = reader.ReadVector3();
                    continue;
                }

                if (SaveBuiltInCapturers.KeyEquals(key, LocalRotationKey) && mask.IsEnabled(1) && type == ESaveKvType.Quaternion)
                {
                    self.localRotation = reader.ReadQuaternion();
                    continue;
                }

                if (SaveBuiltInCapturers.KeyEquals(key, LocalScaleKey) && mask.IsEnabled(2) && type == ESaveKvType.Vector3)
                {
                    self.localScale = reader.ReadVector3();
                    continue;
                }

                reader.SkipRecordPayload();
            }
        }
    }

    /// <summary>
    /// Rigidbody 内置捕获器：linearVelocity/angularVelocity 两字段（物理暂停/恢复助手——存档恢复刚体运动态）。
    /// <para>恢复仅作用于非运动学刚体（运动学刚体速度由动画/脚本驱动，写速度无物理意义）。</para>
    /// </summary>
    public sealed class RigidbodyCapturer : ISaveComponentCapturer
    {
        /// <summary>字段键：线速度。</summary>
        public const string LinearVelocityKey = "linearVelocity";

        /// <summary>字段键：角速度。</summary>
        public const string AngularVelocityKey = "angularVelocity";

        /// <summary>全量字段名数组（索引 = 掩码索引）。</summary>
        private static readonly string[] s_FieldNames = { LinearVelocityKey, AngularVelocityKey };

        /// <inheritdoc />
        public Type ComponentType => typeof(Rigidbody);

        /// <inheritdoc />
        public string[] FieldNames => s_FieldNames;

        /// <inheritdoc />
        public int SchemaVersion => 1;

        /// <inheritdoc />
        public void Capture(object component, ref SaveKeyValueWriter writer, in SaveFieldMask mask)
        {
            var self = (Rigidbody)component;
            int enabledCount = 0;
            for (int i = 0; i < s_FieldNames.Length; i++)
            {
                if (mask.IsEnabled(i))
                {
                    enabledCount++;
                }
            }

            writer.BeginNestedObject(ComponentType.FullName, enabledCount);
            if (mask.IsEnabled(0))
            {
                writer.WriteVector3(LinearVelocityKey, self.linearVelocity);
            }

            if (mask.IsEnabled(1))
            {
                writer.WriteVector3(AngularVelocityKey, self.angularVelocity);
            }

            writer.EndNested();
        }

        /// <inheritdoc />
        public void Restore(object component, ref SaveKeyValueReader reader, int recordCount, in SaveFieldMask mask)
        {
            var self = (Rigidbody)component;
            bool isKinematic = self.isKinematic;
            for (int consumed = 0; consumed < recordCount && reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type); consumed++)
            {
                // 运动学刚体跳过速度写回（无物理意义且引擎不支持）
                if (!isKinematic && SaveBuiltInCapturers.KeyEquals(key, LinearVelocityKey) && mask.IsEnabled(0) && type == ESaveKvType.Vector3)
                {
                    self.linearVelocity = reader.ReadVector3();
                    continue;
                }

                if (!isKinematic && SaveBuiltInCapturers.KeyEquals(key, AngularVelocityKey) && mask.IsEnabled(1) && type == ESaveKvType.Vector3)
                {
                    self.angularVelocity = reader.ReadVector3();
                    continue;
                }

                reader.SkipRecordPayload();
            }
        }
    }

    /// <summary>
    /// ParticleSystem 内置捕获器：time 单字段（粒子播放进度持久化）。
    /// <para>恢复直接写 <see cref="ParticleSystem.time"/>——仅在粒子系统处于播放态时有视觉效果。</para>
    /// </summary>
    public sealed class ParticleSystemCapturer : ISaveComponentCapturer
    {
        /// <summary>字段键：播放时间。</summary>
        public const string TimeKey = "time";

        /// <summary>全量字段名数组（索引 = 掩码索引）。</summary>
        private static readonly string[] s_FieldNames = { TimeKey };

        /// <inheritdoc />
        public Type ComponentType => typeof(ParticleSystem);

        /// <inheritdoc />
        public string[] FieldNames => s_FieldNames;

        /// <inheritdoc />
        public int SchemaVersion => 1;

        /// <inheritdoc />
        public void Capture(object component, ref SaveKeyValueWriter writer, in SaveFieldMask mask)
        {
            var self = (ParticleSystem)component;
            int enabledCount = mask.IsEnabled(0) ? 1 : 0;
            writer.BeginNestedObject(ComponentType.FullName, enabledCount);
            if (mask.IsEnabled(0))
            {
                writer.WriteSingle(TimeKey, self.time);
            }

            writer.EndNested();
        }

        /// <inheritdoc />
        public void Restore(object component, ref SaveKeyValueReader reader, int recordCount, in SaveFieldMask mask)
        {
            var self = (ParticleSystem)component;
            for (int consumed = 0; consumed < recordCount && reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type); consumed++)
            {
                if (SaveBuiltInCapturers.KeyEquals(key, TimeKey) && mask.IsEnabled(0) && type == ESaveKvType.Single)
                {
                    self.time = reader.ReadSingle();
                    continue;
                }

                reader.SkipRecordPayload();
            }
        }
    }
}
