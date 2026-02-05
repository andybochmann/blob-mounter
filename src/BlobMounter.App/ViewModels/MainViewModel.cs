using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using BlobMounter.App.Services;
using BlobMounter.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlobMounter.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MountService _mountService;
    private readonly DriverDetectionService _driverDetectionService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _accountKey = string.Empty;

    [ObservableProperty]
    private string _containerName = string.Empty;

    [ObservableProperty]
    private string _subfolder = string.Empty;

    [ObservableProperty]
    private char _selectedDriveLetter = 'Z';

    [ObservableProperty]
    private bool _readOnly;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<char> AvailableDriveLetters { get; } = new();

    public ObservableCollection<MountedDriveInfo> MountedDrives { get; } = new();

    /// <summary>
    /// Raised after settings are loaded so the view can set the PasswordBox.
    /// </summary>
    public event Action<string>? SettingsLoaded;

    public MainViewModel(MountService mountService, DriverDetectionService driverDetectionService,
        SettingsService settingsService)
    {
        _mountService = mountService;
        _driverDetectionService = driverDetectionService;
        _settingsService = settingsService;
        RefreshAvailableDriveLetters();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        if (settings == null) return;

        AccountName = settings.AccountName;
        AccountKey = settings.AccountKey;
        ContainerName = settings.ContainerName;
        Subfolder = settings.Subfolder;
        ReadOnly = settings.ReadOnly;

        if (settings.DriveLetter != default && AvailableDriveLetters.Contains(settings.DriveLetter))
            SelectedDriveLetter = settings.DriveLetter;

        // Notify the view to populate the PasswordBox
        if (!string.IsNullOrEmpty(settings.AccountKey))
            SettingsLoaded?.Invoke(settings.AccountKey);
    }

    private void SaveSettings()
    {
        _settingsService.Save(new SavedSettings
        {
            AccountName = AccountName,
            AccountKey = AccountKey,
            ContainerName = ContainerName,
            Subfolder = Subfolder,
            DriveLetter = SelectedDriveLetter,
            ReadOnly = ReadOnly,
        });
    }

    private void RefreshAvailableDriveLetters()
    {
        AvailableDriveLetters.Clear();
        var usedLetters = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();

        for (var c = 'D'; c <= 'Z'; c++)
        {
            if (!usedLetters.Contains(c))
                AvailableDriveLetters.Add(c);
        }

        if (AvailableDriveLetters.Count > 0 && !AvailableDriveLetters.Contains(SelectedDriveLetter))
            SelectedDriveLetter = AvailableDriveLetters.Last();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!ValidateInput()) return;

        IsBusy = true;
        StatusMessage = "Testing connection...";

        try
        {
            var client = new Core.Azure.BlobStorageClient(AccountName, AccountKey, ContainerName);
            await client.TestConnectionAsync();
            StatusMessage = "Connection successful!";
            SaveSettings();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MountAsync()
    {
        if (!ValidateInput()) return;

        if (!_driverDetectionService.IsDokanInstalled())
        {
            StatusMessage = "Dokan driver not installed!";
            MessageBox.Show(DriverDetectionService.GetInstallInstructions(),
                "Dokan Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = $"Mounting {SelectedDriveLetter}:...";

        try
        {
            var config = new MountConfiguration
            {
                AccountName = AccountName,
                AccountKey = AccountKey,
                ContainerName = ContainerName,
                Subfolder = string.IsNullOrWhiteSpace(Subfolder) ? null : Subfolder,
                DriveLetter = SelectedDriveLetter,
                ReadOnly = ReadOnly,
            };

            await _mountService.MountAsync(config);

            MountedDrives.Add(new MountedDriveInfo
            {
                DriveLetter = SelectedDriveLetter,
                Container = ContainerName,
                Subfolder = Subfolder,
                IsReadOnly = ReadOnly,
            });

            StatusMessage = $"Mounted {SelectedDriveLetter}: successfully!";
            SaveSettings();
            RefreshAvailableDriveLetters();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mount failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnmountAsync(MountedDriveInfo driveInfo)
    {
        IsBusy = true;
        StatusMessage = $"Unmounting {driveInfo.DriveLetter}:...";

        try
        {
            await _mountService.UnmountAsync(driveInfo.DriveLetter);
            MountedDrives.Remove(driveInfo);
            StatusMessage = $"Unmounted {driveInfo.DriveLetter}: successfully!";
            RefreshAvailableDriveLetters();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unmount failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            StatusMessage = "Account Name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(AccountKey))
        {
            StatusMessage = "Account Key is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ContainerName))
        {
            StatusMessage = "Container Name is required.";
            return false;
        }

        if (ContainerName.Length < 3 || ContainerName.Length > 63)
        {
            StatusMessage = "Container name must be 3-63 characters.";
            return false;
        }

        return true;
    }
}

public class MountedDriveInfo
{
    public char DriveLetter { get; init; }
    public string Container { get; init; } = string.Empty;
    public string Subfolder { get; init; } = string.Empty;
    public bool IsReadOnly { get; init; }

    public string DisplayName => $"{DriveLetter}: → {Container}" +
        (string.IsNullOrWhiteSpace(Subfolder) ? "" : $"/{Subfolder}") +
        (IsReadOnly ? " (Read-Only)" : "");
}
