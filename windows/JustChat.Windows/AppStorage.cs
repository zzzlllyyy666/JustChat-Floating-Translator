using System.IO;

namespace JustChat.Windows;

internal static class AppStorage
{
    public static string DirectoryPath
    {
        get
        {
            var configuredDirectory = Environment.GetEnvironmentVariable("JUSTCHAT_CONFIG_DIRECTORY");
            return !string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.GetFullPath(configuredDirectory)
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "JustChat");
        }
    }
}
