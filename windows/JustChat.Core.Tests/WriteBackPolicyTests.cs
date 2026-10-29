using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class WriteBackPolicyTests
{
    [Theory]
    [InlineData(100u, 200u, true)]
    [InlineData(100u, 100u, false)]
    [InlineData(0u, 200u, false)]
    public void TargetCapture_OnlyAcceptsExternalProcess(
        uint foregroundProcessId,
        uint ownProcessId,
        bool expected)
    {
        var shouldCapture = WriteBackPolicy.ShouldCaptureTarget(
            new nint(1),
            foregroundProcessId,
            ownProcessId);

        Assert.Equal(expected, shouldCapture);
    }

    [Fact]
    public void TargetCapture_RejectsMissingWindowHandle()
    {
        var shouldCapture = WriteBackPolicy.ShouldCaptureTarget(nint.Zero, 100u, 200u);

        Assert.False(shouldCapture);
    }

    [Theory]
    [InlineData(42u, 42u, true)]
    [InlineData(42u, 43u, false)]
    public void ClipboardRestore_RequiresUnchangedSequence(
        uint temporarySequence,
        uint currentSequence,
        bool expected)
    {
        var shouldRestore = WriteBackPolicy.ShouldRestoreClipboard(
            temporarySequence,
            currentSequence);

        Assert.Equal(expected, shouldRestore);
    }
}
