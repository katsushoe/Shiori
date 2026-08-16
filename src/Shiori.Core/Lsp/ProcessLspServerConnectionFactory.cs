using System.Diagnostics;
using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Starts language servers as direct child processes over stdio.</summary>
public sealed class ProcessLspServerConnectionFactory : ILspServerConnectionFactory
{
    /// <inheritdoc />
    public async Task<ILspServerConnection> StartAsync(
        LanguageServerDescriptor descriptor,
        string workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        if (!Path.IsPathFullyQualified(descriptor.ExecutablePath)
            || !File.Exists(descriptor.ExecutablePath))
        {
            throw new FileNotFoundException(
                "Language-server executable is unavailable.",
                descriptor.ExecutablePath);
        }

        var canonicalWorkspace = Path.GetFullPath(workspace);
        if (!Directory.Exists(canonicalWorkspace))
        {
            throw new DirectoryNotFoundException($"Workspace is unavailable: {canonicalWorkspace}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = descriptor.ExecutablePath,
            WorkingDirectory = canonicalWorkspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in descriptor.Arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("The language-server process could not be started.");
        }

        var transport = new LspJsonRpcTransport(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
        var connection = new ProcessLspServerConnection(process, transport);
        try
        {
            await transport.SendRequestAsync(
                "initialize",
                new
                {
                    processId = Environment.ProcessId,
                    rootUri = new Uri(canonicalWorkspace).AbsoluteUri,
                    capabilities = new { },
                },
                cancellationToken).ConfigureAwait(false);
            await transport.SendNotificationAsync("initialized", new { }, cancellationToken)
                .ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ProcessLspServerConnection : ILspServerConnection
    {
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
        private readonly Process _process;
        private readonly LspJsonRpcTransport _transport;
        private readonly Task<string> _stderrTask;
        private bool _disposed;

        internal ProcessLspServerConnection(Process process, LspJsonRpcTransport transport)
        {
            _process = process;
            _transport = transport;
            _stderrTask = process.StandardError.ReadToEndAsync();
        }

        public bool IsAlive => !_disposed && !_process.HasExited;

        public Task<JsonElement> SendRequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken = default) =>
            _transport.SendRequestAsync(method, parameters, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_process.HasExited)
            {
                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                try
                {
                    await _transport.SendRequestAsync("shutdown", null, timeout.Token)
                        .ConfigureAwait(false);
                    await _transport.SendNotificationAsync("exit", null, timeout.Token)
                        .ConfigureAwait(false);
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or LspProtocolException
                    or OperationCanceledException or EndOfStreamException or ObjectDisposedException)
                {
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                }
            }

            await _transport.DisposeAsync().ConfigureAwait(false);
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
            await _stderrTask.ConfigureAwait(false);
            _process.Dispose();
        }
    }
}
