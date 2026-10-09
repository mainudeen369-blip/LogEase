using System.Net.Http.Json;
using System.Text.Json;

namespace LogEase.Api.Integrations.Llm;

public interface ILlmChatClient
{
    Task<LlmChatResponse> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken);
}

public sealed record LlmChatResponse(string Content, string Provider, IReadOnlyList<string> Attempts);

/// <summary>
/// Tries configured models in order. Local OpenAI-compatible servers (Ollama or
/// LM Studio) are intended to be first; paid cloud providers are fallbacks only.
/// </summary>
public sealed class ChainedLlmChatClient : ILlmChatClient
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChainedLlmChatClient> _logger;

    public ChainedLlmChatClient(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ChainedLlmChatClient> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LlmChatResponse> CompleteAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var attempts = new List<string>();

        foreach (var provider in GetProviderOrder())
        {
            try
            {
                var content = provider switch
                {
                    "local" => await CompleteOpenAiCompatibleAsync(
                        "local",
                        _configuration.GetSection("Llm:Local"),
                        systemPrompt,
                        userMessage,
                        cancellationToken),
                    "openai" => await CompleteOpenAiCompatibleAsync(
                        "openai",
                        _configuration.GetSection("Llm:OpenAi"),
                        systemPrompt,
                        userMessage,
                        cancellationToken),
                    "openrouter" => await CompleteOpenAiCompatibleAsync(
                        "openrouter",
                        GetOpenRouterSection(),
                        systemPrompt,
                        userMessage,
                        cancellationToken),
                    "gemini" => await CompleteGeminiAsync(systemPrompt, userMessage, cancellationToken),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(content))
                {
                    EnsureJsonObject(content, provider);
                    return new LlmChatResponse(content, provider, attempts);
                }

                attempts.Add($"{provider}: unavailable or empty response");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
            {
                attempts.Add($"{provider}: {Trim(ex.Message, 180)}");
                _logger.LogWarning(ex, "LLM provider {Provider} failed; trying the next provider.", provider);
            }
        }

        throw new InvalidOperationException(
            attempts.Count == 0
                ? "No LLM providers are enabled."
                : $"No configured LLM provider succeeded. {string.Join(" | ", attempts)}");
    }

    private IEnumerable<string> GetProviderOrder()
    {
        var configuredOrder = _configuration.GetSection("Llm:ProviderOrder").Get<string[]>();
        return (configuredOrder is { Length: > 0 } ? configuredOrder : ["Local", "OpenAi", "OpenRouter", "Gemini"])
            .Select(provider => provider.Trim().ToLowerInvariant())
            .Where(provider => provider is "local" or "openai" or "openrouter" or "gemini")
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IConfigurationSection GetOpenRouterSection()
    {
        var configured = _configuration.GetSection("Llm:OpenRouter");
        return configured.Exists() ? configured : _configuration.GetSection("Gemini");
    }

    private async Task<string?> CompleteOpenAiCompatibleAsync(
        string provider,
        IConfiguration section,
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var baseUrl = section["BaseUrl"]
                      ?? (provider == "openrouter" ? "https://openrouter.ai/api/v1" : null);
        var model = section["Model"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            return null;

        var apiKey = ResolveApiKey(section, provider);
        var enabled = section.GetValue<bool?>("Enabled")
                      ?? (provider == "local" || !string.IsNullOrWhiteSpace(apiKey));
        if (!enabled)
            return null;

        if (provider != "local" && string.IsNullOrWhiteSpace(apiKey))
            return null;

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("TimeoutSeconds", 90), 5, 300));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/chat/completions");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

        if (provider == "openrouter")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://logease.local");
            request.Headers.TryAddWithoutValidation("X-Title", "LogEase");
        }

        request.Content = JsonContent.Create(new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = "INPUT_JSON:\n" + userMessage },
            },
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{provider} HTTP {(int)response.StatusCode}: {Trim(body, 300)}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private async Task<string?> CompleteGeminiAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var section = _configuration.GetSection("Llm:Gemini");
        if (!section.Exists())
            section = _configuration.GetSection("Gemini");

        if (!section.GetValue<bool?>("Enabled").GetValueOrDefault(!string.IsNullOrWhiteSpace(section["ApiKey"])))
            return null;

        var apiKey = ResolveApiKey(section, "gemini");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var model = section["Model"] ?? "gemini-2.0-flash";
        if (model.Contains('/'))
            model = model[(model.LastIndexOf('/') + 1)..];

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(section.GetValue("TimeoutSeconds", 90), 5, 300));
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var payload = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = systemPrompt + "\n\nINPUT_JSON:\n" + userMessage } } },
            },
            generationConfig = new { temperature = 0.2, responseMimeType = "application/json" },
        };

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"gemini HTTP {(int)response.StatusCode}: {Trim(body, 300)}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }

    private string? ResolveApiKey(IConfiguration section, string provider) =>
        section["ApiKey"]
        ?? Environment.GetEnvironmentVariable(provider switch
        {
            "openai" => "OPENAI_API_KEY",
            "openrouter" => "OPENROUTER_API_KEY",
            "gemini" => "GEMINI_API_KEY",
            _ => "OLLAMA_API_KEY",
        })
        ?? (provider == "gemini" ? Environment.GetEnvironmentVariable("GOOGLE_API_KEY") : null);

    private static void EnsureJsonObject(string content, string provider)
    {
        var json = content.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
            json = System.Text.RegularExpressions.Regex.Replace(
                json,
                @"^```(?:json)?\s*|\s*```$",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{provider} did not return a JSON object.");
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
