using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Backends
{
    public readonly struct LeaderboardEntry
    {
        public LeaderboardEntry(string playerId, long score)
        {
            PlayerId = playerId;
            Score = score;
        }

        public string PlayerId { get; }
        public long Score { get; }
    }

    public interface ILeaderboardBackend
    {
        UniTask<IReadOnlyList<LeaderboardEntry>> ReadTopAsync(string boardId, int count,
            CallOptions call, CancellationToken cancellationToken);
        UniTask SubmitAsync(string boardId, long score, CallOptions call,
            CancellationToken cancellationToken);
    }

    public interface ITelemetryBackend
    {
        UniTask EnqueueAsync(string eventName, IReadOnlyDictionary<string, string> fields,
            CallOptions call, CancellationToken cancellationToken);
    }

    public readonly struct RemoteConfigEntry
    {
        public RemoteConfigEntry(string key, string value, long revision, bool expired)
        {
            Key = key;
            Value = value;
            Revision = revision;
            Expired = expired;
        }

        public string Key { get; }
        public string Value { get; }
        public long Revision { get; }
        public bool Expired { get; }
    }

    public interface IRemoteConfigBackend
    {
        UniTask<IReadOnlyList<RemoteConfigEntry>> FetchAsync(long knownRevision,
            CallOptions call, CancellationToken cancellationToken);
    }

    public enum SessionStatus : byte { Joined, Expired, Unavailable }

    public readonly struct SessionResult
    {
        public SessionResult(SessionStatus status, string sessionId, long sequence)
        {
            Status = status;
            SessionId = sessionId;
            Sequence = sequence;
        }

        public SessionStatus Status { get; }
        public string SessionId { get; }
        public long Sequence { get; }
        public bool Joined => Status == SessionStatus.Joined && SessionId != null;
    }

    public interface ISessionBackend
    {
        UniTask<SessionResult> JoinAsync(string sessionCode, CallOptions call,
            CancellationToken cancellationToken);
        UniTask<SessionResult> ResumeAsync(string sessionId, long confirmedSequence,
            CallOptions call, CancellationToken cancellationToken);
        UniTask LeaveAsync(string sessionId, CallOptions call, CancellationToken cancellationToken);
    }
}
