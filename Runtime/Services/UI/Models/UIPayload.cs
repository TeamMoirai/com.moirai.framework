namespace Moirai.Atropos.UI
{
    /// <summary>动态腿唯一擦除载体：运行期 Type 已知、TArg 未知的开窗路把载荷装在这一份里过手。</summary>
    /// <remarks>
    /// 布局：一个引用 + 一段字节档（字节档为将来的基元位内联预留，本版不做——静态腿 <c>in TArg</c> 才是基元与 struct 的主路）。 <br />
    /// <see cref="Empty"/> 与 <c>null</c> 引用同判；引用型只存引用（0 分配），值类型装箱一次（仅动态腿）。
    /// </remarks>
    public readonly struct UIPayload
    {
        private enum EKind : byte { Empty = 0, Value = 1 }

        private readonly object _ref;
        private readonly EKind _kind;

        private UIPayload(object value)
        {
            _ref = value;
            _kind = EKind.Value;
        }

        /// <summary>空载荷：动态腿的缺省值。</summary>
        public static UIPayload Empty => default;

        /// <summary>是否为空载荷。</summary>
        public bool IsEmpty => _kind == EKind.Empty;

        /// <summary>装一份载荷：<c>null</c> 归约为 <see cref="Empty"/>；引用型只存引用，值类型装箱一次。</summary>
        public static UIPayload From(object value) => value == null ? Empty : new UIPayload(value);

        /// <summary>按 <typeparamref name="T"/> 取载荷。</summary>
        /// <exception cref="GameException">类型不符，或空载荷作用于值类型（消息带期望类型名）。</exception>
        public T To<T>()
        {
            if (_kind == EKind.Empty)
            {
                if (default(T) == null)
                {
                    return default;
                }

                throw new GameException(StringUtility.Format(
                    "UIPayload 为空，取不出值类型 {0}：调用方没塞载荷。", typeof(T).Name));
            }

            if (_ref is T typed)
            {
                return typed;
            }

            throw new GameException(StringUtility.Format(
                "UIPayload 载荷类型不符：期望 {0}，实际 {1}。", typeof(T).Name, _ref.GetType().FullName));
        }

        /// <summary>尝试按 <typeparamref name="T"/> 取载荷；空载荷或类型不符回假，不抬错。</summary>
        public bool TryGet<T>(out T value)
        {
            if (_kind != EKind.Empty && _ref is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }
    }
}
