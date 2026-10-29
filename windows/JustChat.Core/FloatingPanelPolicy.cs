namespace JustChat.Core;

public static class FloatingPanelPolicy
{
    public static bool ShouldHideOnToggle(bool isVisible, bool isActive) =>
        isVisible && isActive;
}
