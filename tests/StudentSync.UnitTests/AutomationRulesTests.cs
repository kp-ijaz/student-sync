using System.Security.Cryptography;
using System.Text;
using StudentSync.Application.Automation;
using StudentSync.Application.Fees;
using StudentSync.Contracts;
using StudentSync.Domain.Platform;

namespace StudentSync.UnitTests;

/// <summary>Pure rules behind Settings ▸ Automations (owner request 2026-11-17).</summary>
public sealed class AutomationRulesTests
{
    [Theory]
    [InlineData("98765 43210", "IN", "+919876543210")]
    [InlineData("098765-43210", "IN", "+919876543210")]
    [InlineData("+91 98765 43210", "AE", "+919876543210")]
    [InlineData("0091 98765 43210", "AE", "+919876543210")]
    [InlineData("919876543210", "IN", "+919876543210")]
    [InlineData("050 123 4567", "AE", "+971501234567")]
    [InlineData("50 123 4567", "AE", "+971501234567")]
    [InlineData("(971) 50-123-4567", "AE", "+971501234567")]
    public void Phone_numbers_typed_at_admission_become_international(string raw, string country, string expected) =>
        Assert.Equal(expected, PhoneNumbers.ToE164(raw, country));

    [Theory]
    [InlineData(null, "IN")]
    [InlineData("", "IN")]
    [InlineData("12345", "IN")]
    [InlineData("98765 43210", "")]      // local number, unknown country: never guess
    [InlineData("98765 43210", "US")]
    [InlineData("call dad", "IN")]
    [InlineData("5123 4567", "IN")]      // Indian mobiles start with 6–9
    public void Unreadable_numbers_are_skipped_not_guessed(string? raw, string country) => Assert.Null(PhoneNumbers.ToE164(raw, country));

    [Fact]
    public void Masked_numbers_keep_only_the_country_and_last_five_digits() =>
        Assert.Equal("+91•••••43210", PhoneNumbers.Mask("+919876543210"));

    [Theory]
    [InlineData(5, null)]
    [InlineData(3, "before")]
    [InlineData(1, "before")]
    [InlineData(0, "today")]
    [InlineData(-1, null)]
    [InlineData(-2, null)]
    [InlineData(-3, "after")]
    [InlineData(-40, "after")]
    [InlineData(-61, null)]              // old debts never flood the day automations are switched on
    public void Fee_reminders_go_before_on_and_after_the_due_day(int daysToDue, string? stage) =>
        Assert.Equal(stage, DailyAutomations.Stage(new AutomationSettings { FeeReminderDaysBefore = 3, FeeReminderDaysAfter = 3 }, daysToDue));

    [Fact]
    public void Zero_days_switches_the_before_and_after_reminders_off()
    {
        var s = new AutomationSettings { FeeReminderDaysBefore = 0, FeeReminderDaysAfter = 0 };
        Assert.Null(DailyAutomations.Stage(s, 1));
        Assert.Equal("today", DailyAutomations.Stage(s, 0));
        Assert.Null(DailyAutomations.Stage(s, -5));
    }

    [Theory]
    [InlineData("2026-11-17T10:00:00Z", "2026-11-17T10:00:00Z")]   // 15:30 in India: send now
    [InlineData("2026-11-17T16:00:00Z", "2026-11-18T02:30:00Z")]   // 21:30 in India: wait for 08:00 next day
    [InlineData("2026-11-17T01:00:00Z", "2026-11-17T02:30:00Z")]   // 06:30 in India: wait for 08:00 today
    public void Sms_are_never_sent_between_9pm_and_8am_centre_time(string now, string expected) =>
        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), SmsOutbox.NextAllowedUtc(DateTime.Parse(now).ToUniversalTime(), "Asia/Kolkata"));

    [Fact]
    public void Razorpay_webhooks_need_the_institutes_signature()
    {
        const string body = """{"event":"payment_link.paid","payload":{"payment_link":{"entity":{"id":"plink_1"}}}}""";
        var good = Convert.ToHexStringLower(HMACSHA256.HashData("secret"u8.ToArray(), Encoding.UTF8.GetBytes(body)));
        Assert.True(PaymentWebhooks.VerifyRazorpay(body, good, "secret"));
        Assert.False(PaymentWebhooks.VerifyRazorpay(body, good, "other-secret"));
        Assert.False(PaymentWebhooks.VerifyRazorpay(body + " ", good, "secret"));
        Assert.False(PaymentWebhooks.VerifyRazorpay(body, null, "secret"));
        Assert.Equal("plink_1", PaymentWebhooks.LinkIdOf(PaymentProvider.Razorpay, body));
    }

    [Fact]
    public void Stripe_webhooks_need_a_fresh_valid_signature()
    {
        const string body = """{"type":"checkout.session.completed","data":{"object":{"payment_link":"plink_9","payment_status":"paid"}}}""";
        var now = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var t = new DateTimeOffset(now).ToUnixTimeSeconds();
        string Sign(long ts, string secret) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{body}")));
        Assert.True(PaymentWebhooks.VerifyStripe(body, $"t={t},v1={Sign(t, "whsec_x")}", "whsec_x", now));
        Assert.False(PaymentWebhooks.VerifyStripe(body, $"t={t},v1={Sign(t, "whsec_y")}", "whsec_x", now));
        // Replayed later than 5 minutes: refused.
        Assert.False(PaymentWebhooks.VerifyStripe(body, $"t={t},v1={Sign(t, "whsec_x")}", "whsec_x", now.AddMinutes(6)));
        Assert.False(PaymentWebhooks.VerifyStripe(body, "v1=abc", "whsec_x", now));
        Assert.Equal("plink_9", PaymentWebhooks.LinkIdOf(PaymentProvider.Stripe, body));
        Assert.Null(PaymentWebhooks.LinkIdOf(PaymentProvider.Stripe, "not json"));
    }

    [Theory]
    [InlineData(6000, "INR", false, "₹6,000")]
    [InlineData(6000, "INR", true, "Rs. 6,000")]
    [InlineData(1250.5, "AED", false, "AED 1,250.50")]
    [InlineData(1250.5, "AED", true, "AED 1,250.50")]
    public void Money_in_messages_is_plain_and_sms_safe(decimal amount, string currency, bool sms, string expected) =>
        Assert.Equal(expected, Fmt.Money(amount, currency, sms));

    [Fact]
    public void Receipt_numbers_are_stable_and_readable() =>
        Assert.Equal("R-202611-ABCDEF", ReceiptService.Number("R", new DateOnly(2026, 11, 17), Guid.Parse("00000000-0000-0000-0000-000000abcdef")));
}
