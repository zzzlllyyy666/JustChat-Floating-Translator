using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JustChat.Windows;

public sealed class DeepSeekCredentialStore
{
    private readonly string _credentialPath;

    public DeepSeekCredentialStore()
    {
        _credentialPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JustChat",
            "deepseek.key");
    }

    public string? Load()
    {
        if (!File.Exists(_credentialPath))
        {
            return null;
        }

        byte[]? plaintext = null;
        try
        {
            var encrypted = Convert.FromBase64String(File.ReadAllText(_credentialPath).Trim());
            plaintext = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is
            FormatException or
            CryptographicException or
            IOException or
            UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "无法读取已保存的 API Key，请重新填写并保存",
                exception);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API Key 不能为空", nameof(apiKey));
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = Encoding.UTF8.GetBytes(apiKey.Trim());
            var encrypted = ProtectedData.Protect(
                plaintext,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_credentialPath)!);
            File.WriteAllText(
                _credentialPath,
                Convert.ToBase64String(encrypted),
                Encoding.ASCII);
        }
        catch (Exception exception) when (exception is
            CryptographicException or
            IOException or
            UnauthorizedAccessException)
        {
            throw new IOException(
                "API Key 保存失败，请检查当前用户的存储权限",
                exception);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}
