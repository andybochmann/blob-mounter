using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlobMounter.App.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlobMounter");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public SavedSettings? Load()
    {
        if (!File.Exists(SettingsPath))
            return null;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var dto = JsonSerializer.Deserialize<SettingsDto>(json);
            if (dto == null) return null;

            string? accountKey = null;
            if (dto.ProtectedAccountKey is { Length: > 0 })
            {
                try
                {
                    var encryptedBytes = Convert.FromBase64String(dto.ProtectedAccountKey);
                    var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    accountKey = Encoding.UTF8.GetString(decryptedBytes);
                }
                catch
                {
                    // Key couldn't be decrypted (different user/machine) — leave blank
                }
            }

            return new SavedSettings
            {
                AccountName = dto.AccountName ?? string.Empty,
                AccountKey = accountKey ?? string.Empty,
                ContainerName = dto.ContainerName ?? string.Empty,
                Subfolder = dto.Subfolder ?? string.Empty,
                DriveLetter = dto.DriveLetter,
                ReadOnly = dto.ReadOnly,
            };
        }
        catch
        {
            return null;
        }
    }

    public void Save(SavedSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);

        string? protectedKey = null;
        if (!string.IsNullOrEmpty(settings.AccountKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(settings.AccountKey);
            var encryptedBytes = ProtectedData.Protect(keyBytes, null, DataProtectionScope.CurrentUser);
            protectedKey = Convert.ToBase64String(encryptedBytes);
        }

        var dto = new SettingsDto
        {
            AccountName = settings.AccountName,
            ProtectedAccountKey = protectedKey,
            ContainerName = settings.ContainerName,
            Subfolder = settings.Subfolder,
            DriveLetter = settings.DriveLetter,
            ReadOnly = settings.ReadOnly,
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private sealed class SettingsDto
    {
        public string? AccountName { get; set; }
        public string? ProtectedAccountKey { get; set; }
        public string? ContainerName { get; set; }
        public string? Subfolder { get; set; }
        public char DriveLetter { get; set; }
        public bool ReadOnly { get; set; }
    }
}

public sealed class SavedSettings
{
    public string AccountName { get; init; } = string.Empty;
    public string AccountKey { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string Subfolder { get; init; } = string.Empty;
    public char DriveLetter { get; init; }
    public bool ReadOnly { get; init; }
}
