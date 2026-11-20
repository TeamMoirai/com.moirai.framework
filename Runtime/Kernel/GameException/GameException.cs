using System;
using System.Runtime.Serialization;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架统一异常类型。
    /// </summary>
    [Serializable]
    public class GameException : Exception
    {
        public GameException()
            : base() { }
        public GameException(string message)
            : base(message)
        {
        }
        public GameException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
        protected GameException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
