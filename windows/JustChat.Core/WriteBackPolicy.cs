namespace JustChat.Core;

public static class WriteBackPolicy
{
    public static bool ShouldCaptureTarget(
        nint windowHandle,
        uint foregroundProcessId,
        uint ownProcessId) =>
        windowHandle != nint.Zero &&
        foregroundProcessId != 0 &&
        foregroundProcessId != ownProcessId;

    public static bool ShouldRestoreClipboard(uint temporarySequence, uint currentSequence) =>
        temporarySequence == currentSequence;
}
