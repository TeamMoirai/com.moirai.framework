using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Backends
{
    public enum AuthStatus : byte { Authenticated, Rejected, Unavailable }

    public readonly struct SignInRequest
    {
        public SignInRequest(string account, string credential)
        {
            Account = account;
            Credential = credential;
        }

        public string Account { get; }
        public string Credential { get; }
    }

    public readonly struct AuthResult
    {
        public AuthResult(AuthStatus status, string token) { Status = status; Token = token; }
        public AuthStatus Status { get; }
        public string Token { get; }
        public bool Succeeded => Status == AuthStatus.Authenticated && Token != null;
    }

    public interface IAuthBackend
    {
        UniTask<AuthResult> SignInAsync(SignInRequest request, CallOptions call,
            CancellationToken cancellationToken);
    }
}
