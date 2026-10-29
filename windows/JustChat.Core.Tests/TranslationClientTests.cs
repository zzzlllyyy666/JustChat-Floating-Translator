using System.Net;
using System.Text;
using System.Text.Json;
using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class TranslationClientTests
{
    [Fact]
    public async Task TranslateAsync_SendsDeepSeekRequestAndReturnsTranslation()
    {
        var (client, handler) = CreateClient(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.com/chat/completions", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);

            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Assert.Equal("deepseek-flash", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.Equal(4096, root.GetProperty("max_tokens").GetInt32());
            Assert.False(root.TryGetProperty("maxTokens", out var _));
            Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
            Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());

            var messages = root.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Contains("Japanese", messages[0].GetProperty("content").GetString());
            Assert.Equal("user", messages[1].GetProperty("role").GetString());

            using var userDocument = JsonDocument.Parse(
                messages[1].GetProperty("content").GetString()!);
            var user = userDocument.RootElement;
            Assert.Equal("请确认交期", user.GetProperty("text_to_translate").GetString());
            Assert.Equal(string.Empty, user.GetProperty("locked_prefix").GetString());
            var preferences = user.GetProperty("translation_preferences");
            Assert.Equal("跨境电商", preferences.GetProperty("industry").GetString());
            Assert.Equal("自然、专业，不要太客套", preferences.GetProperty("tone").GetString());

            return TranslationResponse(new { translation = "納期をご確認ください。" });
        });

        var result = await client.TranslateAsync(
            "请确认交期",
            TranslationTargetLanguage.Japanese,
            new TranslationPreferences("跨境电商", "自然、专业，不要太客套"),
            "test-key");

        Assert.Equal("納期をご確認ください。", result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_MissingApiKeyDoesNotSendRequest()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult(TranslationResponse(new { translation = "Hello" })));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync("你好", TranslationTargetLanguage.English, apiKey: "  "));

        Assert.Equal(TranslationClientErrorKind.MissingApiKey, exception.ErrorKind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_EmptyDraftDoesNotSendRequest()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult(TranslationResponse(new { translation = "Hello" })));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync(" \r\n ", TranslationTargetLanguage.English, apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.EmptyDraft, exception.ErrorKind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_DraftOverUtf16LimitDoesNotSendRequest()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult(TranslationResponse(new { translation = "Hello" })));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync(
                new string('中', TranslationClient.MaximumDraftLength + 1),
                TranslationTargetLanguage.English,
                apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.DraftTooLong, exception.ErrorKind);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData(401, "API Key")]
    [InlineData(402, "余额不足")]
    [InlineData(429, "请求过多")]
    [InlineData(504, "超时")]
    public async Task TranslateAsync_HttpFailuresHaveActionableMessages(
        int statusCode,
        string expectedMessage)
    {
        var (client, handler) = CreateClient((_, _) => Task.FromResult(Response(
            "{\"error\":{\"message\":\"engine unavailable\"}}",
            (HttpStatusCode)statusCode)));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync("你好", TranslationTargetLanguage.English, apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.Server, exception.ErrorKind);
        Assert.Contains(expectedMessage, exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_InvalidOuterJsonProducesInvalidResponse()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult(Response("not-json")));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync("你好", TranslationTargetLanguage.English, apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.InvalidResponse, exception.ErrorKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_IncompleteFirstResponseRetriesWithCompleteInput()
    {
        var (client, handler) = CreateClient(async (request, requestCount) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var systemPrompt = document.RootElement
                .GetProperty("messages")[0]
                .GetProperty("content").GetString();
            if (requestCount == 2)
            {
                Assert.Contains("previous attempt was rejected", systemPrompt);
            }

            return TranslationResponse(new
            {
                translation = requestCount == 1 ? "你好" : "Hello"
            });
        });

        var result = await client.TranslateAsync(
            "你好",
            TranslationTargetLanguage.English,
            apiKey: "test-key");

        Assert.Equal("Hello", result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_RepeatedIncompleteResponsesFailAfterTwoRequests()
    {
        var (client, handler) = CreateClient((_, _) =>
            Task.FromResult(TranslationResponse(new { translation = "你好" })));

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync("你好", TranslationTargetLanguage.English, apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.IncompleteTranslation, exception.ErrorKind);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_TimeoutHasActionableMessage()
    {
        var (client, handler) = CreateClient(async (_, _) =>
        {
            await Task.Yield();
            throw new TaskCanceledException("Simulated timeout");
        });

        var exception = await Assert.ThrowsAsync<TranslationClientException>(() =>
            client.TranslateAsync("你好", TranslationTargetLanguage.English, apiKey: "test-key"));

        Assert.Equal(TranslationClientErrorKind.Server, exception.ErrorKind);
        Assert.Contains("超时", exception.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    private static (TranslationClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, int, Task<HttpResponseMessage>> handler)
    {
        var stub = new StubHttpMessageHandler(handler);
        var client = new TranslationClient(
            new HttpClient(stub),
            new Uri("https://example.com/chat/completions"));
        return (client, stub);
    }

    private static HttpResponseMessage TranslationResponse(object payload)
    {
        return ChatResponse(JsonSerializer.Serialize(payload));
    }

    private static HttpResponseMessage ChatResponse(string content)
    {
        return Response(JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content } }
            }
        }));
    }

    private static HttpResponseMessage Response(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _handler;
        private int _requestCount;

        internal StubHttpMessageHandler(
            Func<HttpRequestMessage, int, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        internal int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestCount = Interlocked.Increment(ref _requestCount);
            return _handler(request, requestCount);
        }
    }
}
