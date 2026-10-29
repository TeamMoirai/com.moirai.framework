using System;

namespace Moirai.Atropos.Storage
{
    public sealed class StorageCommitIndeterminateException : Exception
    {
        public StorageCommitIndeterminateException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
