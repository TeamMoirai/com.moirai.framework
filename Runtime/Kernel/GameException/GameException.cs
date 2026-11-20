using System;
using System.Runtime.Serialization;

namespace Moirai.Atropos
{
    /// <summary>Phase 5 剥离（T10）：命名已重塑为 <see cref="MoiraiException"/>。继承关系保持——既有 catch(GameException) 继续生效。</summary>
    [Serializable]
    [System.Obsolete("Phase 5 剥离（T10）：请迁移至 MoiraiException（继承关系保持，既有 catch 继续生效）。")]
    public class GameException : MoiraiException
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
