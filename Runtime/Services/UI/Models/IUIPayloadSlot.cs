namespace Moirai.Atropos.UI
{
    /// <summary>载荷槽的内部泛型桥：账本的泛型直塞通道经它把载荷按 <typeparamref name="TArg"/> 落进窗口，不经过 <see cref="UIPayload"/> 擦除。</summary>
    /// <remarks>两轨泛型基类实现它；泛型虚方法覆写绑不上类级 TArg，接口桥是零装箱正解。</remarks>
    internal interface IUIPayloadSlot<TArg>
    {
        /// <summary>静态腿专用：泛型直塞，struct 不装箱。</summary>
        void SetPayload(in TArg payload);
    }
}
