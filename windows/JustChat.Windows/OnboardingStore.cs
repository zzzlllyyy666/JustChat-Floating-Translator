using System.IO;

namespace JustChat.Windows;

internal static class OnboardingStore
{
    private static string MarkerPath => Path.Combine(AppStorage.DirectoryPath, "onboarding.complete");

    public static bool IsComplete => File.Exists(MarkerPath);

    public static void Complete()
    {
        Directory.CreateDirectory(AppStorage.DirectoryPath);
        File.WriteAllText(MarkerPath, "1");
    }
}
