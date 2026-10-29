using System.IO;
using System.Text;
using JustChat.Core;

namespace JustChat.Windows;

internal sealed class TranslationPreferencesStore
{
    private readonly string _preferencesPath;

    public TranslationPreferencesStore()
    {
        _preferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JustChat",
            "preferences.json");
    }

    public TranslationPreferences Load()
    {
        if (!File.Exists(_preferencesPath))
        {
            return TranslationPreferences.Empty;
        }

        try
        {
            return TranslationPreferencesJson.Deserialize(
                File.ReadAllText(_preferencesPath));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "无法读取已保存的翻译偏好，请重新保存",
                exception);
        }
    }

    public void Save(TranslationPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_preferencesPath)!;
        var temporaryPath = Path.Combine(
            directory,
            $"preferences.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporaryPath,
                TranslationPreferencesJson.Serialize(preferences),
                Encoding.UTF8);
            File.Move(temporaryPath, _preferencesPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            throw new IOException(
                "翻译偏好保存失败，请检查当前用户的存储权限",
                exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original save error.
        }
    }
}
