namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档加载结果（<c>TryLoad</c> 返回值）：区分「无档」「损坏」「解密失败」等错误类别，供调用方精确兜底。
    /// <para>成功时 <see cref="IsSuccess"/> 为 <c>true</c> 且 <see cref="Data"/> 为反序列化对象；失败时 <see cref="Error"/> 标明原因，<see cref="Data"/> 为默认值。</para>
    /// </summary>
    /// <typeparam name="T">存档数据类型。</typeparam>
    public readonly struct SaveResult<T>
    {
        private readonly T _data;
        private readonly SaveError _error;

        /// <summary>
        /// 加载成功时的存档数据；失败时为默认值。
        /// </summary>
        public T Data => _data;

        /// <summary>
        /// 错误码；成功时为 <see cref="SaveError.None"/>。
        /// </summary>
        public SaveError Error => _error;

        /// <summary>
        /// 是否加载成功。
        /// </summary>
        public bool IsSuccess => _error == SaveError.None;

        /// <summary>
        /// 创建加载结果。
        /// </summary>
        /// <param name="data">存档数据。</param>
        /// <param name="error">错误码。</param>
        private SaveResult(T data, SaveError error)
        {
            _data = data;
            _error = error;
        }

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        /// <param name="data">反序列化后的存档数据。</param>
        /// <returns>成功结果。</returns>
        public static SaveResult<T> Success(T data)
        {
            return new SaveResult<T>(data, SaveError.None);
        }

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>失败结果。</returns>
        public static SaveResult<T> Failure(SaveError error)
        {
            return new SaveResult<T>(default, error);
        }
    }
}
