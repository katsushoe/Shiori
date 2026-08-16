using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Shiori.Core.Lsp;

/// <summary>Exchanges LSP JSON-RPC requests and responses over framed streams.</summary>
public sealed class LspJsonRpcTransport : IAsyncDisposable
{
    private const int MaximumHeaderLength = 8 * 1024;
    private const int MaximumContentLength = 16 * 1024 * 1024;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly CancellationTokenSource _disposeSource = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _readerLock = new();
    private Task? _readerTask;
    private long _nextRequestId;
    private bool _disposed;

    /// <summary>Initializes a transport over readable and writable streams.</summary>
    public LspJsonRpcTransport(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (!input.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(input));
        if (!output.CanWrite) throw new ArgumentException("Output stream must be writable.", nameof(output));
        _input = input;
        _output = output;
    }

    /// <summary>Sends a JSON-RPC request and waits for its matching response.</summary>
    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("A duplicate LSP request ID was generated.");
        }

        EnsureReaderStarted();
        using var registration = cancellationToken.Register(
            () => CancelRequest(id, cancellationToken));
        try
        {
            await WriteRequestAsync(id, method, parameters, cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _disposeSource.CancelAsync().ConfigureAwait(false);
        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposeSource.IsCancellationRequested)
            {
            }
        }

        FailPending(new ObjectDisposedException(nameof(LspJsonRpcTransport)));
        _writeLock.Dispose();
        _disposeSource.Dispose();
        await _input.DisposeAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureReaderStarted()
    {
        lock (_readerLock)
        {
            _readerTask ??= ReadResponsesAsync(_disposeSource.Token);
        }
    }

    private async Task WriteRequestAsync(
        long id,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var request = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        };
        var content = JsonSerializer.SerializeToUtf8Bytes(request);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {content.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadResponsesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await ReadResponseAsync(cancellationToken).ConfigureAwait(false))
            {
            }

            FailPending(new EndOfStreamException("The language server closed its output stream."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or JsonException or LspProtocolException)
        {
            FailPending(exception);
        }
    }

    private async Task<bool> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var contentLength = await ReadContentLengthAsync(cancellationToken).ConfigureAwait(false);
        if (contentLength is null) return false;

        var content = new byte[contentLength.Value];
        await _input.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return true;
        }

        if (!_pending.TryRemove(id, out var completion)) return true;
        if (root.TryGetProperty("error", out var error))
        {
            completion.TrySetException(new LspProtocolException(error.GetRawText()));
            return true;
        }

        var result = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : JsonSerializer.SerializeToElement<object?>(null);
        completion.TrySetResult(result);
        return true;
    }

    private async Task<int?> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var current = new byte[1];
        while (bytes.Count < MaximumHeaderLength)
        {
            var read = await _input.ReadAsync(current, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (bytes.Count == 0) return null;
                throw new LspProtocolException("The LSP header ended unexpectedly.");
            }

            bytes.Add(current[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n'
                && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
            {
                return ParseContentLength(Encoding.ASCII.GetString([.. bytes]));
            }
        }

        throw new LspProtocolException("The LSP header exceeds the maximum length.");
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Content-Length:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(line[prefix.Length..].Trim(), CultureInfo.InvariantCulture, out var length)
                && length >= 0 && length <= MaximumContentLength)
            {
                return length;
            }
        }

        throw new LspProtocolException("The LSP Content-Length header is missing or invalid.");
    }

    private void CancelRequest(long id, CancellationToken cancellationToken)
    {
        if (_pending.TryRemove(id, out var completion))
        {
            completion.TrySetCanceled(cancellationToken);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var request in _pending)
        {
            if (_pending.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }
}
