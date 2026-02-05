using System.Collections.Concurrent;
using BlobMounter.Core.Azure;
using BlobMounter.Core.FileSystem;
using BlobMounter.Core.Models;
using DokanNet;
using DokanNet.Logging;

namespace BlobMounter.App.Services;

public sealed class MountService : IDisposable
{
    private readonly ConcurrentDictionary<char, MountInfo> _activeMounts = new();

    public IReadOnlyDictionary<char, MountInfo> ActiveMounts => _activeMounts;

    public async Task MountAsync(MountConfiguration config)
    {
        if (_activeMounts.ContainsKey(config.DriveLetter))
            throw new InvalidOperationException($"Drive {config.DriveLetter}: is already mounted.");

        var client = new BlobStorageClient(config.AccountName, config.AccountKey, config.ContainerName);

        // Validate connection first
        await client.TestConnectionAsync();

        var pathMapper = new PathMapper(config.Subfolder);
        var cache = new MetadataCache();
        var fileSystem = new BlobFileSystem(client, pathMapper, cache, config.ContainerName, config.ReadOnly);

        var mountPoint = $"{config.DriveLetter}:\\";
        var cts = new CancellationTokenSource();

        var mountThread = new Thread(() =>
        {
            try
            {
                var dokanBuilder = new DokanInstanceBuilder(new Dokan(new NullLogger()))
                    .ConfigureOptions(options =>
                    {
                        options.MountPoint = mountPoint;
                        options.Options = DokanOptions.FixedDrive;
                        options.SingleThread = false;
                    });
                using var instance = dokanBuilder.Build(fileSystem);
                instance.WaitForFileSystemClosed(uint.MaxValue);
            }
            catch (Exception)
            {
                // Mount failed or was unmounted
            }
            finally
            {
                _activeMounts.TryRemove(config.DriveLetter, out _);
            }
        })
        {
            Name = $"Dokan-{config.DriveLetter}",
            IsBackground = true,
        };

        var mountInfo = new MountInfo(config, mountThread, cts);
        _activeMounts[config.DriveLetter] = mountInfo;
        mountThread.Start();

        // Give Dokan a moment to initialize
        await Task.Delay(500);
    }

    public Task UnmountAsync(char driveLetter)
    {
        if (!_activeMounts.TryRemove(driveLetter, out var mountInfo))
            throw new InvalidOperationException($"Drive {driveLetter}: is not mounted.");

        try
        {
            var dokan = new Dokan(new NullLogger());
            dokan.RemoveMountPoint($"{driveLetter}:\\");
        }
        catch
        {
            // Best effort unmount
        }

        mountInfo.CancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var kvp in _activeMounts)
        {
            try
            {
                var dokan = new Dokan(new NullLogger());
                dokan.RemoveMountPoint($"{kvp.Key}:\\");
            }
            catch
            {
                // Best effort
            }

            kvp.Value.CancellationTokenSource.Cancel();
        }

        _activeMounts.Clear();
    }
}

public sealed record MountInfo(
    MountConfiguration Configuration,
    Thread MountThread,
    CancellationTokenSource CancellationTokenSource);

/// <summary>
/// Null logger for Dokan — suppresses all log output.
/// </summary>
internal sealed class NullLogger : ILogger
{
    public bool DebugEnabled => false;

    public void Debug(string message, params object[] args) { }
    public void Info(string message, params object[] args) { }
    public void Warn(string message, params object[] args) { }
    public void Error(string message, params object[] args) { }
    public void Fatal(string message, params object[] args) { }
}
