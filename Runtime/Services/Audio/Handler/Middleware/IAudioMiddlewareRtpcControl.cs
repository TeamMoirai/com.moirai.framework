namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 可选桥接能力：设置实时参数（FMOD event parameter / Wwise RTPC）。
    /// </summary>
    /// <remarks>不走 <see cref="IAudioMiddlewareBridge"/> 主接口，理由见 <see cref="IAudioMiddlewareBankControl"/>。</remarks>
    internal interface IAudioMiddlewareRtpcControl
    {
        /// <summary>
        /// 设置参数。
        /// </summary>
        /// <param name="instanceId">原生实例 ID；0 表示工程/全局作用域（不绑定单个实例）。</param>
        void SetRtpc(string name, float value, ulong instanceId);
    }
}
