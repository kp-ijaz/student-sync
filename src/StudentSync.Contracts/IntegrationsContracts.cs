namespace StudentSync.Contracts;

// Institute connections to outside services (admin Settings ▸ Integrations). Secrets are write-only: requests may carry
// them, responses only say whether one is saved. An empty secret in a save request keeps the saved one.

public enum SmsProvider { Twilio, Msg91 }
public enum PaymentProvider { Razorpay, Stripe }

/// <summary>Result of the last "send test" / "verify keys" for a connection.</summary>
public sealed record IntegrationCheckDto(DateTime CheckedAtUtc, bool Ok, string Message);

public sealed record EmailIntegrationDto(bool Enabled, string? Host, int Port, bool UseStartTls, string? Username, bool HasPassword,
    string? FromAddress, string? FromName, IntegrationCheckDto? LastCheck);
public sealed record SaveEmailIntegrationRequest(bool Enabled, string? Host, int Port, bool UseStartTls, string? Username, string? Password,
    string? FromAddress, string? FromName);

/// <summary>Twilio: AccountId = Account SID, Sender = phone number (+…) or Messaging Service SID (MG…), secret = Auth Token.
/// MSG91: Sender = 6-letter sender ID (optional), TemplateId = DLT flow/template ID, secret = auth key.</summary>
public sealed record SmsIntegrationDto(bool Enabled, SmsProvider Provider, string? AccountId, string? Sender, string? TemplateId, bool HasSecret,
    IntegrationCheckDto? LastCheck);
public sealed record SaveSmsIntegrationRequest(bool Enabled, SmsProvider Provider, string? AccountId, string? Sender, string? TemplateId, string? Secret);

/// <summary>Razorpay: KeyId = Key ID (rzp_test_/rzp_live_), secret = Key Secret. Stripe: KeyId = publishable key (pk_…),
/// secret = secret key (sk_… or restricted rk_…). Mode ("test"/"live") comes from the key prefix.</summary>
public sealed record PaymentIntegrationDto(bool Enabled, PaymentProvider Provider, string? KeyId, string? Mode, bool HasSecret, bool HasWebhookSecret,
    IntegrationCheckDto? LastCheck);
public sealed record SavePaymentIntegrationRequest(bool Enabled, PaymentProvider Provider, string? KeyId, string? Secret, string? WebhookSecret);

public sealed record InstitutionIntegrationsDto(EmailIntegrationDto Email, SmsIntegrationDto Sms, PaymentIntegrationDto Payments, bool ServerEmailConfigured);

public sealed record TestEmailRequest(string ToAddress);
public sealed record TestSmsRequest(string ToPhone);
