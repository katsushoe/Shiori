using System.Diagnostics;

namespace Shiori.Cli.Server;

internal sealed class WindowsTerminalIndexLauncher : IIndexTerminalLauncher
{
    public void Launch(string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The Shiori executable path is unavailable.");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }
        var terminalPath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        var startInfo = CreateStartInfo(terminalPath, executablePath, workspace);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Terminal could not be started.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string terminalPath,
        string executablePath,
        string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        var startInfo = new ProcessStartInfo
        {
            FileName = terminalPath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("new-tab");
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add($"Shiori index: {workspace}");
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add("index");
        startInfo.ArgumentList.Add("rebuild");
        startInfo.ArgumentList.Add("--allow");
        startInfo.ArgumentList.Add(workspace);
        return startInfo;
    }
}
