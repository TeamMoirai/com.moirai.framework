using System;

namespace Moirai.Atropos.Backends.Policies
{
    public enum CloudCasVerdict : byte { Write, Conflict, Unknown }

    public static class CloudCasDecision
    {
        public static CloudCasVerdict Decide(long? expectedVersion, long currentVersion,
            bool remoteReachable)
        {
            if (!remoteReachable) return CloudCasVerdict.Unknown;
            if (expectedVersion == null) return currentVersion == 0
                ? CloudCasVerdict.Write : CloudCasVerdict.Conflict;
            return expectedVersion.Value == currentVersion
                ? CloudCasVerdict.Write : CloudCasVerdict.Conflict;
        }
    }
}
