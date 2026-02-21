using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SynologyPhotoFrame");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AppSettings> LoadAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var json = await File.ReadAllTextAsync(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _semaphore.WaitAsync();
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(SettingsFile, json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public string EncryptPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(password);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string DecryptPassword(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword)) return string.Empty;
        try
        {
            var encrypted = Convert.FromBase64String(encryptedPassword);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
