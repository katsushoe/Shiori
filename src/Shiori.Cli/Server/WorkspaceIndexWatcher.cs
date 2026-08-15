using Shiori.Core.Engine;

namespace Shiori.Cli.Server;

/// <summary>Debounces workspace changes into serialized incremental index builds.</summary>
internal sealed class WorkspaceIndexWatcher : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);
    private readonly string _workspace;
    private readonly Func<IShioriEngine> _getEngine;
    private readonly Action<string, Exception>? _onError;
    private readonly TimeSpan _debounce;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    internal WorkspaceIndexWatcher(
        string workspace,
        Func<IShioriEngine> getEngine,
        Action<string, Exception>? onError = null,
        TimeSpan? debounce = null)
    {
        _workspace = workspace;
        _getEngine = getEngine;
        _onError = onError;
        _debounce = debounce ?? DefaultDebounce;
        if (_debounce < TimeSpan.FromMilliseconds(100) || _debounce > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(debounce), "Debounce must be from 100 ms to 10 seconds.");
        }

        _timer = new Timer(static state => ((WorkspaceIndexWatcher)state!).QueueBuild(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(workspace)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs) => Schedule();

    private void OnRenamed(object sender, RenamedEventArgs eventArgs) => Schedule();

    private void OnError(object sender, ErrorEventArgs eventArgs)
    {
        _onError?.Invoke(_workspace, eventArgs.GetException());
        Schedule();
    }

    private void Schedule()
    {
        if (!_disposed)
        {
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueBuild() => _ = BuildIndexAsync();

    private async Task BuildIndexAsync()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _gate.WaitAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!_stopping.IsCancellationRequested)
            {
                _getEngine().BuildIndex();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onError?.Invoke(_workspace, exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
        _stopping.Cancel();
        _gate.Wait();
        _gate.Release();
        _stopping.Dispose();
    }
}
