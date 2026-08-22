namespace Shiori.Cli.Server;

/// <summary>Resumes index generations left incomplete by an earlier process.</summary>
internal sealed class InterruptedIndexResumeService(
    NativeEngineRegistry engines,
    IIndexTerminalLauncher terminalLauncher,
    ILogger<InterruptedIndexResumeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interrupted = await new WorkspaceRegistry()
            .ListInterruptedAsync(stoppingToken)
            .ConfigureAwait(false);
        foreach (var workspace in interrupted)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    logger.LogInformation(
                        "Launching Windows Terminal to resume index for {WorkspacePath}.",
                        workspace.Path);
                    terminalLauncher.Launch(workspace.Path);
                    continue;
                }

                logger.LogInformation("Resuming interrupted index for {WorkspacePath}.", workspace.Path);
                var engine = engines.GetEngine(workspace.Path);
                var totalDirectories = await Task.Run(
                    () => engine.CountIndexDirectories(),
                    stoppingToken).ConfigureAwait(false);
                var status = await Task.Run(
                    () => engine.BuildIndex(totalDirectories),
                    stoppingToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Interrupted index resumed for {WorkspacePath}; {FileCount} files are active.",
                    workspace.Path,
                    status.IndexedFiles);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not resume interrupted index for {WorkspacePath}.", workspace.Path);
            }
        }
    }
}
