namespace RoboCamHub.App;

internal static class PackagedRuntimeBootstrap
{
    internal static void Configure()
    {
        var applicationDirectory = AppContext.BaseDirectory;
        var pluginDirectory = Path.Combine(applicationDirectory, "gstreamer-1.0");
        if (!Directory.Exists(pluginDirectory))
        {
            return;
        }

        // Keep packaged builds independent from developer-machine plugin registries and
        // system installations. Only the plugins selected by the packager are visible.
        Environment.SetEnvironmentVariable("GST_PLUGIN_SYSTEM_PATH_1_0", pluginDirectory);
        Environment.SetEnvironmentVariable("GST_PLUGIN_PATH_1_0", string.Empty);

        var scannerName = OperatingSystem.IsWindows()
            ? "gst-plugin-scanner.exe"
            : "gst-plugin-scanner";
        var scannerPath = Path.Combine(pluginDirectory, scannerName);
        if (File.Exists(scannerPath))
        {
            Environment.SetEnvironmentVariable("GST_PLUGIN_SCANNER_1_0", scannerPath);
        }

        if (OperatingSystem.IsWindows())
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable(
                "PATH",
                string.IsNullOrEmpty(currentPath)
                    ? applicationDirectory
                    : $"{applicationDirectory}{Path.PathSeparator}{currentPath}");
        }
    }
}
