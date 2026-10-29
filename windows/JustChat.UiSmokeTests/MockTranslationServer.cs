using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace JustChat.UiSmokeTests;

internal sealed class MockTranslationServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _receiveLoop;
    private int _requestCount;

    public MockTranslationServer()
    {
        var portListener = new TcpListener(IPAddress.Loopback, 0);
        portListener.Start();
        var port = ((IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();

        Endpoint = new Uri($"http://127.0.0.1:{port}/chat/completions");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _receiveLoop = ReceiveAsync();
    }

    public Uri Endpoint { get; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    private async Task ReceiveAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_cancellation.IsCancellationRequested)
            {
                break;
            }

            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        Interlocked.Increment(ref _requestCount);
        try
        {
            using var reader = new StreamReader(
                context.Request.InputStream,
                context.Request.ContentEncoding);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            using var request = JsonDocument.Parse(body);
            var messages = request.RootElement.GetProperty("messages");
            var userContent = messages.EnumerateArray()
                .Last(message => message.GetProperty("role").GetString() == "user")
                .GetProperty("content")
                .GetString() ?? throw new InvalidDataException("Missing user content");
            using var prompt = JsonDocument.Parse(userContent);

            string content;
            if (prompt.RootElement.TryGetProperty("segments", out var segments))
            {
                var translations = segments.EnumerateArray().Select(segment => new
                {
                    id = segment.GetProperty("id").GetInt32(),
                    translation = Translate(segment.GetProperty("text").GetString() ?? string.Empty)
                }).ToArray();
                content = JsonSerializer.Serialize(new { translations });
            }
            else
            {
                var source = prompt.RootElement.GetProperty("text_to_translate").GetString() ?? string.Empty;
                content = JsonSerializer.Serialize(new { translation = Translate(source) });
            }

            var response = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { role = "assistant", content } }
                }
            });
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                error = new { message = exception.Message }
            }));
            context.Response.StatusCode = 400;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static string Translate(string source) => source switch
    {
        "第一段" => "First paragraph",
        "第二段" => "Second paragraph",
        "第三段" => "Third paragraph",
        "第四段" => "Fourth paragraph",
        _ => "Translated smoke-test draft"
    };

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try
        {
            _receiveLoop.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The listener is intentionally stopped during cleanup.
        }

        _listener.Close();
        _cancellation.Dispose();
    }
}
