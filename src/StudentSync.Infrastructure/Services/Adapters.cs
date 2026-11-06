using System.Linq.Expressions;
using Hangfire;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StudentSync.Application.Common;

namespace StudentSync.Infrastructure.Services;

// ---------------- File storage ----------------

public sealed class StorageOptions
{
    public string LocalRootPath { get; set; } = ".storage";
}

/// <summary>
/// Private file storage on the local file system (development / single-node). Files are never served statically;
/// they are streamed by the API only after authorisation. Swap for an S3/Azure Blob implementation of <see cref="IFileStorage"/> in production.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private string PathFor(string key)
    {
        if (key.Contains("..") || Path.IsPathRooted(key)) throw new ArgumentException("Invalid storage key.");
        var root = Path.GetFullPath(options.Value.LocalRootPath);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root, StringComparison.Ordinal)) throw new ArgumentException("Invalid storage key.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(fs, ct);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        => Task.FromResult<Stream>(new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}

/// <summary>Short-lived, tamper-proof download tokens bound to file, user and tenant (ASP.NET Core Data Protection).</summary>
public sealed class FileAccessTokens(IDataProtectionProvider provider) : IFileAccessTokens
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("StudentSync.FileAccess.v1").ToTimeLimitedDataProtector();

    public string Create(Guid fileId, Guid userId, Guid tenantId, TimeSpan lifetime)
        => _protector.Protect($"{fileId:N}|{userId:N}|{tenantId:N}", lifetime);

    public bool TryValidate(string token, out Guid fileId, out Guid userId, out Guid tenantId)
    {
        fileId = userId = tenantId = Guid.Empty;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            return parts.Length == 3 && Guid.TryParse(parts[0], out fileId) && Guid.TryParse(parts[1], out userId) && Guid.TryParse(parts[2], out tenantId);
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
}

// ---------------- Email ----------------

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@studentsync.example.test";
    public string FromName { get; set; } = "Student Sync";
}

/// <summary>SMTP delivery. Reports Accepted only when the SMTP server accepts the message.</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.Host);

    public async Task<DeliveryResult> SendAsync(EmailMessage m, CancellationToken ct)
    {
        if (!IsConfigured) return new DeliveryResult(false, null, "Email provider not configured.");
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(new MailboxAddress(m.ToName, m.ToAddress));
        message.Subject = m.Subject;
        message.Body = new TextPart("plain") { Text = m.TextBody };
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(o.Host!, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(o.Username)) await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
            var response = await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return new DeliveryResult(true, message.MessageId ?? response, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SMTP delivery failed");
            return new DeliveryResult(false, null, ex.GetType().Name + ": " + ex.Message);
        }
    }
}

/// <summary>No push provider (e.g. Firebase Cloud Messaging) credentials are configured; nothing is sent.</summary>
public sealed class NotConfiguredPushSender : IPushSender
{
    public bool IsConfigured => false;
    public Task<DeliveryResult> SendAsync(string deviceToken, string title, string body, string? link, CancellationToken ct)
        => Task.FromResult(new DeliveryResult(false, null, "Push provider not configured."));
}

public sealed class NotConfiguredTranscriptionProvider : ITranscriptionProvider
{
    public bool IsConfigured => false;
    public string ProviderName => "none";
    public Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken ct)
        => Task.FromResult(new TranscriptionResult(false, null, [], "No transcription provider is configured."));
}

public sealed class NotConfiguredLanguageModel : ILanguageModel
{
    public bool IsConfigured => false;
    public string ProviderName => "none";
    public string ModelName => "none";
    public Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
        => Task.FromResult(new LlmResult(false, null, 0, 0, "No language model provider is configured."));
}

// ---------------- Background jobs ----------------

public sealed class HangfireBackgroundJobs(IBackgroundJobClient client) : IBackgroundJobs
{
    public string Enqueue<T>(Expression<Func<T, Task>> job) => client.Enqueue(job);
    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay) => client.Schedule(job, delay);
}
