using System;

namespace Moirai.Atropos.Persistence
{
    public sealed class SaveException : Exception
    {
        public SaveException(SaveError error, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public SaveError Error { get; }
    }
}
