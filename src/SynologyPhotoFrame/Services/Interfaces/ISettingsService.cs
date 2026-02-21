using SynologyPhotoFrame.Models;

namespace SynologyPhotoFrame.Services.Interfaces;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
    string EncryptPassword(string password);
    string DecryptPassword(string encryptedPassword);
}
