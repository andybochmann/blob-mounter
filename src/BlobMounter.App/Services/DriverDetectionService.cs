using System.IO;

namespace BlobMounter.App.Services;

public sealed class DriverDetectionService
{
    public bool IsDokanInstalled()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var dokanDll = Path.Combine(system32, "dokan2.dll");
        return File.Exists(dokanDll);
    }

    public static string GetInstallInstructions()
    {
        return """
            The Dokan driver is required but not installed.

            Please download and install Dokan from:
            https://github.com/dokan-dev/dokany/releases

            Install the latest DokanSetup.exe, then restart this application.
            """;
    }
}
