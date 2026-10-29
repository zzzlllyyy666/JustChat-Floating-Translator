using JustChat.Core;

namespace JustChat.Windows;

internal static class AppTranslationClient
{
    public static TranslationClient Create()
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("JUSTCHAT_TRANSLATION_ENDPOINT");
        return Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint) &&
            (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
            ? new TranslationClient(endpoint: endpoint)
            : new TranslationClient();
    }
}
