using StudentSync.Domain.Common;

namespace StudentSync.Domain.Fees;

public enum PaymentLinkStatus { Open, Paid, Expired, Cancelled }

/// <summary>
/// An online payment link (Razorpay Payment Link or Stripe Checkout) for what is owed on one package. It is marked paid
/// only from the gateway itself — a signed webhook or the server asking the gateway — never from the payer's browser.
/// The resulting <see cref="FeePayment"/> (method Online, reference = gateway payment id) is created exactly once.
/// </summary>
public class PaymentLink : TenantEntity
{
    public Guid PackageId { get; set; }
    public Guid StudentId { get; set; }
    /// <summary>"Razorpay" or "Stripe".</summary>
    public string Provider { get; set; } = "";
    public string ProviderLinkId { get; set; } = "";
    public string Url { get; set; } = "";
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "";
    public PaymentLinkStatus Status { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? ProviderPaymentId { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public Guid? FeePaymentId { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
}
