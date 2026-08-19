using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// JSON 处理器基类。
    /// </summary>
    [Serializable]
    public abstract class JsonHandler
    {
        [NonSerialized]
        private bool _initialized;

        internal void Internal_Init()
        {
            if (_initialized) return;

            OnInit();
            _initialized = true;
        }

        internal void Internal_Shutdown()
        {
            if (!_initialized) return;

            _initialized = false;
            Shutdown();
        }

        protected abstract void OnInit();

        protected abstract void Shutdown();

        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则以提高可读性的格式输出。如果为 <c>false</c>，则为紧凑格式输出。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        public abstract string ToJson(object obj, bool prettyPrint = false);

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public abstract T ToObject<T>(string json);

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public abstract object ToObject(Type objectType, string json);

        /// <summary>
        /// 使用 JSON 覆盖对象。
        /// </summary>
        /// <param name="json"></param>
        /// <param name="objectToOverwrite"></param>
        /// <remarks>将 JSON 数据反序列化到现有对象上，并覆盖现有数据</remarks>
        public abstract void FromJsonOverwrite(string json, object objectToOverwrite);
    }

    /// <summary>
    /// 字节通路 JSON 能力接口（可选实现）。
    /// </summary>
    /// <remarks>
    /// <para>面向 IO/网络等天然以字节为载体的场景（存档、加密、上行/下行报文），
    /// 序列化直接产出 UTF8 字节、反序列化直接消费 UTF8 字节，跳过 string 中间态的
    /// UTF16↔UTF8 双向转码与大字符串分配。</para>
    /// <para>能力探测：<see cref="JsonUtility"/> 门面以 <c>Handler is IBufferJsonHandler</c>
    /// 探测；未实现者（如 Newtonsoft handler）自动回退 string 路径
    /// （<see cref="System.Text.Encoding"/>.UTF8 编解码），调用方无感。</para>
    /// <para>语义约束：字节输出必须与 string 输出 UTF8 编码后逐字节等价（紧凑格式）；
    /// 字节解析必须接受与 string 解析相同的输入集合（含 legacy 字典格式、带引号历史数值）。</para>
    /// </remarks>
    public interface IBufferJsonHandler
    {
        /// <summary>将对象序列化为 UTF8 JSON 字节（紧凑格式）。</summary>
        byte[] ToJsonBytes(object obj);

        /// <summary>将 UTF8 JSON 字节反序列化为对象。</summary>
        T ToObject<T>(byte[] json);

        /// <summary>将 UTF8 JSON 字节反序列化为对象。</summary>
        object ToObject(Type objectType, byte[] json);
    }
}