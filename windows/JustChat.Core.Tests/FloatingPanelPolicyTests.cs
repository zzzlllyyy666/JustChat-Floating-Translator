using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class FloatingPanelPolicyTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Toggle_HidesOnlyVisibleActivePanel(
        bool isVisible,
        bool isActive,
        bool expected)
    {
        var shouldHide = FloatingPanelPolicy.ShouldHideOnToggle(isVisible, isActive);

        Assert.Equal(expected, shouldHide);
    }
}
