using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JustChat.Core;

public sealed class TranslationClient
{
    public const int MaximumDraftLength = 30_000;
    public const string DefaultModel = "deepseek-flash";

    private static readonly Uri DefaultEndpoint =
        new("https://api.deepseek.com/chat/completions");
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _model;

    public TranslationClient(
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        string model = DefaultModel)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _endpoint = endpoint ?? DefaultEndpoint;
        _model = model;
    }

    public async Task<string> TranslateAsync(
        string? text,
        TranslationTargetLanguage targetLanguage,
        TranslationPreferences? preferences = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var trimmedApiKey = apiKey?.Trim();
        if (string.IsNullOrEmpty(trimmedApiKey))
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.MissingApiKey,
                "请先填写 DeepSeek API Key");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.EmptyDraft,
                "请输入需要翻译的内容");
        }

        if (text.Length > MaximumDraftLength)
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.DraftTooLong,
                "中文草稿过长，请分段后再翻译");
        }

        var effectivePreferences = preferences ?? TranslationPreferences.Empty;
        var boundary = TranslationBoundary.ResolveTranslationBoundary(
            text,
            targetLanguage: targetLanguage);
        if (!TranslationBoundary.HasChineseCharacters(boundary.TranslatableText))
        {
            return TranslationBoundary.JoinLockedPrefix(
                boundary.LockedPrefix,
                TranslationBoundary.TranslateChinesePunctuation(
                    boundary.TranslatableText,
                    targetLanguage),
                targetLanguage);
        }

        var repeatedDraft = TranslationBoundary.SplitExactRepeatedDraft(boundary.TranslatableText);
        var sourceForModel = repeatedDraft?.Unit ?? boundary.TranslatableText;
        var segmentedPlan = sourceForModel.Contains('\n')
            ? TranslationBoundary.MakeSegmentedTranslationPlan(sourceForModel)
            : null;
        var normalizedTranslation = string.Empty;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var messages = segmentedPlan is null
                ? TranslationPromptBuilder.CreateMessages(
                    boundary.LockedPrefix,
                    sourceForModel,
                    targetLanguage,
                    effectivePreferences,
                    attempt > 0)
                : TranslationPromptBuilder.CreateSegmentedMessages(
                    boundary.LockedPrefix,
                    segmentedPlan,
                    targetLanguage,
                    effectivePreferences,
                    attempt > 0);
            var content = await SendAsync(
                messages,
                trimmedApiKey,
                cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                continue;
            }

            if (segmentedPlan is not null)
            {
                var segments = ParseSegmentedTranslation(content);
                if (segments is null)
                {
                    continue;
                }

                normalizedTranslation = TranslationBoundary.AssembleSegmentedTranslation(
                    segmentedPlan,
                    segments,
                    boundary.LockedPrefix,
                    targetLanguage) ?? string.Empty;
            }
            else
            {
                var translation = ParseTranslation(content);
                if (translation is null)
                {
                    continue;
                }

                normalizedTranslation = TranslationBoundary.NormalizeTargetTranslation(
                    translation,
                    sourceForModel,
                    boundary.LockedPrefix,
                    targetLanguage);
            }

            if (TranslationBoundary.IsCompleteTargetTranslation(
                    sourceForModel,
                    normalizedTranslation,
                    targetLanguage))
            {
                break;
            }

            normalizedTranslation = string.Empty;
        }

        if (normalizedTranslation.Length == 0)
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.IncompleteTranslation,
                "翻译结果不完整，请重试");
        }

        var completeTranslation = repeatedDraft is null
            ? normalizedTranslation
            : string.Join(
                repeatedDraft.Separator,
                Enumerable.Repeat(normalizedTranslation, repeatedDraft.Count));
        if (!TranslationBoundary.IsCompleteTargetTranslation(
                boundary.TranslatableText,
                completeTranslation,
                targetLanguage))
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.IncompleteTranslation,
                "翻译结果不完整，请重试");
        }

        var result = TranslationBoundary.JoinLockedPrefix(
            boundary.LockedPrefix,
            completeTranslation,
            targetLanguage).Trim();
        if (result.Length == 0)
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.EmptyTranslation,
                "翻译结果为空，请重试");
        }

        return result;
    }

    private async Task<string?> SendAsync(
        DeepSeekMessage[] messages,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestBody = new DeepSeekRequest(_model, messages);
        var json = JsonSerializer.Serialize(requestBody, RequestJsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(timeoutSource.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationClientException(
                    TranslationClientErrorKind.Server,
                    HttpErrorMessage(response.StatusCode, payload));
            }

            ChatResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<ChatResponse>(payload, ResponseJsonOptions);
            }
            catch (JsonException exception)
            {
                throw InvalidResponse(exception);
            }

            if (result?.Choices is not { } choices)
            {
                throw InvalidResponse();
            }

            return choices.FirstOrDefault()?.Message?.Content;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.Server,
                "翻译超时，中文草稿已保留，请重试",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationClientException(
                TranslationClientErrorKind.Server,
                "无法连接 DeepSeek，请检查网络后重试",
                exception);
        }
    }

    private static string? ParseTranslation(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<SingleTranslation>(content, ResponseJsonOptions)
                ?.Translation;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TranslationSegment[]? ParseSegmentedTranslation(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("translations", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var segments = new List<TranslationSegment>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var id) ||
                    !id.TryGetInt32(out var segmentId) ||
                    !item.TryGetProperty("translation", out var translation) ||
                    translation.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                segments.Add(new TranslationSegment(
                    segmentId,
                    translation.GetString() ?? string.Empty));
            }

            return segments.ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string HttpErrorMessage(HttpStatusCode statusCode, string payload)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "API Key 无效或没有使用权限，请检查 API Key",
            HttpStatusCode.PaymentRequired =>
                "翻译账户余额不足，请在 DeepSeek 账户中查看",
            HttpStatusCode.TooManyRequests =>
                "翻译请求过多，请稍后重试",
            HttpStatusCode.GatewayTimeout =>
                "翻译超时，中文草稿已保留，请重试",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable =>
                "翻译引擎暂时不可用，请稍后重试",
            _ => ParseApiErrorMessage(payload) ??
                 $"DeepSeek 返回错误（{(int)statusCode}）"
        };
    }

    private static string? ParseApiErrorMessage(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiErrorResponse>(payload, ResponseJsonOptions)
                ?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TranslationClientException InvalidResponse(Exception? innerException = null)
    {
        return new TranslationClientException(
            TranslationClientErrorKind.InvalidResponse,
            "没有收到有效的翻译响应",
            innerException);
    }

    private sealed record DeepSeekRequest(string Model, DeepSeekMessage[] Messages)
    {
        public ThinkingOptions Thinking { get; } = new();
        public ResponseFormatOptions ResponseFormat { get; } = new();
        public int MaxTokens { get; } = 4096;
        public bool Stream { get; } = false;
    }

    private sealed record ThinkingOptions
    {
        public string Type { get; } = "disabled";
    }

    private sealed record ResponseFormatOptions
    {
        public string Type { get; } = "json_object";
    }

    private sealed record ChatResponse
    {
        public ChatChoice[]? Choices { get; init; }
    }

    private sealed record ChatChoice
    {
        public ChatMessage? Message { get; init; }
    }

    private sealed record ChatMessage
    {
        public string? Content { get; init; }
    }

    private sealed record SingleTranslation
    {
        public string? Translation { get; init; }
    }

    private sealed record ApiErrorResponse
    {
        public ApiError? Error { get; init; }
    }

    private sealed record ApiError
    {
        public string? Message { get; init; }
        public string? Code { get; init; }
    }
}
