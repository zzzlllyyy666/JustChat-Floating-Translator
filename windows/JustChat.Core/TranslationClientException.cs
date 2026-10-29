namespace JustChat.Core;

public enum TranslationClientErrorKind
{
    MissingApiKey,
    EmptyDraft,
    DraftTooLong,
    InvalidResponse,
    IncompleteTranslation,
    EmptyTranslation,
    Server
}

public sealed class TranslationClientException : Exception
{
    public TranslationClientException(
        TranslationClientErrorKind errorKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
    }

    public TranslationClientErrorKind ErrorKind { get; }
}
