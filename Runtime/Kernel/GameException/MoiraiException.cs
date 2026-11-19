using System;
using System.Runtime.Serialization;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架统一异常类型（Phase 5 剥离 T10：脱离外部框架同形命名）。
    /// </summary>
    [Serializable]
    public class MoiraiException : Exception
    {
        public MoiraiException()
            : base() { }
        public MoiraiException(string message)
            : base(message)
        {
        }
        public MoiraiException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
        protected MoiraiException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
