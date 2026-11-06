using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudentSync.Application.Common;

namespace StudentSync.Infrastructure.Services;

public sealed class AnthropicOptions
{
    /// <summary>API key; supply via environment/secret store (Ai__Anthropic__ApiKey). Empty means the provider is not configured.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>USD per million tokens, used only for the usage-cost estimate shown to administrators.</summary>
    public decimal InputPricePerMillion { get; set; } = 4.00m;
    public decimal OutputPricePerMillion { get; set; } = 20.00m;
}

/// <summary>
/// Claude via the official Anthropic SDK. Uses the server-side refusal fallback ("default" routing) so a safety
/// refusal can be retried on another model by the API; a final refusal is reported as a failure, never as content.
/// </summary>
public sealed class ClaudeLanguageModel(IOptions<AnthropicOptions> options, ILogger<ClaudeLanguageModel> logger) : ILanguageModel
{
    private readonly AnthropicOptions _o = options.Value;
    private AnthropicClient? _client;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ApiKey);
    public string ProviderName => "anthropic";
    public string ModelName => _o.Model;

    public async Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken ct)
    {
        if (!IsConfigured) return new LlmResult(false, null, 0, 0, "Anthropic API key is not configured.");
        _client ??= new AnthropicClient { ApiKey = _o.ApiKey };
        try
        {
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = _o.Model,
                MaxTokens = maxTokens,
                System = systemPrompt,
                Messages = [new BetaUserMessageParam { Content = userPrompt }],
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            }, cancellationToken: ct);

            var input = (int)response.Usage.InputTokens;
            var output = (int)response.Usage.OutputTokens;
            var cost = Math.Round(input * _o.InputPricePerMillion / 1_000_000m + output * _o.OutputPricePerMillion / 1_000_000m, 6);
            var stop = response.StopReason?.ToString()?.Replace("_", "");
            if (string.Equals(stop, "refusal", StringComparison.OrdinalIgnoreCase))
                return new LlmResult(false, null, input, output, "The AI provider declined this request.", cost);
            if (string.Equals(stop, "maxtokens", StringComparison.OrdinalIgnoreCase))
                return new LlmResult(false, null, input, output, "The AI response was cut off before it finished.", cost);

            var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            return new LlmResult(true, text, input, output, null, cost);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogWarning("Anthropic API error {Status}", ex.GetType().Name);
            return new LlmResult(false, null, 0, 0, $"AI provider error ({ex.GetType().Name}).");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Anthropic API unreachable");
            return new LlmResult(false, null, 0, 0, "The AI provider could not be reached.");
        }
    }
}
