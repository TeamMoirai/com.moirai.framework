using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Backends
{
    public enum PaymentStatus : byte { Succeeded, Pending, Unknown, Failed }

    public readonly struct PurchaseRequest
    {
        public PurchaseRequest(string purchaseKey, string productId)
        {
            PurchaseKey = purchaseKey;
            ProductId = productId;
        }

        public string PurchaseKey { get; }
        public string ProductId { get; }
    }

    public readonly struct PaymentResult
    {
        public PaymentResult(PaymentStatus status, string receipt)
        {
            Status = status;
            Receipt = receipt;
        }

        public PaymentStatus Status { get; }
        public string Receipt { get; }
        public bool Succeeded => Status == PaymentStatus.Succeeded && Receipt != null;
    }

    public interface IPaymentBackend
    {
        UniTask<PaymentResult> PurchaseAsync(PurchaseRequest request, CallOptions call,
            CancellationToken cancellationToken);
        UniTask<PaymentResult> ReconcileAsync(string purchaseKey, CallOptions call,
            CancellationToken cancellationToken);
    }
}
