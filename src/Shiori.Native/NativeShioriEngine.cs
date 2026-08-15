using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Shiori.Core.Engine;

namespace Shiori.Native;

/// <summary>Calls the Rust search engine through its versioned C ABI.</summary>
public sealed class NativeShioriEngine : IShioriEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ShioriEngineHandle _handle;
    private bool _disposed;

    private NativeShioriEngine(ShioriEngineHandle handle, uint abiVersion)
    {
        _handle = handle;
        AbiVersion = abiVersion;
    }

    /// <inheritdoc />
    public uint AbiVersion { get; }

    /// <inheritdoc />
    public WorkspaceInfo GetWorkspaceInfo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var status = NativeAbi.GetWorkspaceInfo(_handle, out var result, out var error);
        if (status != 0)
        {
            throw CreateException(error, "Native workspace lookup failed.");
        }

        try
        {
            return JsonSerializer.Deserialize<WorkspaceInfo>(ReadBuffer(result), JsonOptions)
                ?? throw new ShioriEngineException("Native engine returned invalid workspace information.");
        }
        finally
        {
            NativeAbi.FreeBuffer(result);
        }
    }

    /// <inheritdoc />
    public IndexStatus GetIndexStatus() => ReadIndexStatus(NativeAbi.GetIndexStatus, "Native index status failed.");

    /// <inheritdoc />
    public IndexStatus BuildIndex() => ReadIndexStatus(NativeAbi.BuildIndex, "Native index build failed.");

    /// <inheritdoc />
    public IndexStatus RebuildIndex() => ReadIndexStatus(NativeAbi.RebuildIndex, "Native index rebuild failed.");

    /// <inheritdoc />
    public unsafe FileOutline GetFileOutline(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = Encoding.UTF8.GetBytes(path);
        fixed (byte* pointer = bytes)
        {
            var status = NativeAbi.GetFileOutline(
                _handle,
                pointer,
                (nuint)bytes.Length,
                out var result,
                out var error);
            if (status != 0)
            {
                throw CreateException(error, "Native file outline failed.");
            }

            try
            {
                return JsonSerializer.Deserialize<FileOutline>(ReadBuffer(result), JsonOptions)
                    ?? throw new ShioriEngineException("Native engine returned an invalid file outline.");
            }
            finally
            {
                NativeAbi.FreeBuffer(result);
            }
        }
    }

    /// <summary>Opens the native engine for an explicitly allowed workspace.</summary>
    public static unsafe NativeShioriEngine Open(string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        var abiVersion = NativeAbi.GetAbiVersion();
        if (abiVersion != NativeAbi.SupportedAbiVersion)
        {
            throw new ShioriEngineException(
                $"Native ABI {abiVersion} is incompatible with host ABI {NativeAbi.SupportedAbiVersion}.");
        }

        var bytes = Encoding.UTF8.GetBytes(workspace);
        fixed (byte* pointer = bytes)
        {
            var status = NativeAbi.Open(pointer, (nuint)bytes.Length, out var nativeHandle, out var error);
            if (status != 0)
            {
                throw CreateException(error, "Native engine failed to open the workspace.");
            }

            var handle = new ShioriEngineHandle(nativeHandle);
            return new NativeShioriEngine(handle, abiVersion);
        }
    }

    /// <inheritdoc />
    public unsafe IReadOnlyList<SearchResult> SearchFiles(string query, int limit = 20)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

        var bytes = Encoding.UTF8.GetBytes(query);
        fixed (byte* pointer = bytes)
        {
            var status = NativeAbi.SearchFiles(
                _handle,
                pointer,
                (nuint)bytes.Length,
                (nuint)limit,
                out var result,
                out var error);
            if (status != 0)
            {
                throw CreateException(error, "Native file search failed.");
            }

            try
            {
                return JsonSerializer.Deserialize<SearchResponse>(ReadBuffer(result), JsonOptions)?.Results
                    ?? throw new ShioriEngineException("Native engine returned an invalid response.");
            }
            finally
            {
                NativeAbi.FreeBuffer(result);
            }
        }
    }

    /// <inheritdoc />
    public unsafe IReadOnlyList<SearchResult> SearchText(
        string query,
        string? path = null,
        string? glob = null,
        bool regex = false,
        bool caseSensitive = false,
        int contextLines = 0,
        int limit = 20)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(contextLines, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contextLines, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);

        var request = JsonSerializer.Serialize(
            new { query, path, glob, regex, caseSensitive, contextLines, limit },
            JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(request);
        fixed (byte* pointer = bytes)
        {
            var status = NativeAbi.SearchText(
                _handle,
                pointer,
                (nuint)bytes.Length,
                out var result,
                out var error);
            if (status != 0)
            {
                throw CreateException(error, "Native text search failed.");
            }

            try
            {
                return JsonSerializer.Deserialize<SearchResponse>(ReadBuffer(result), JsonOptions)?.Results
                    ?? throw new ShioriEngineException("Native engine returned an invalid response.");
            }
            finally
            {
                NativeAbi.FreeBuffer(result);
            }
        }
    }

    /// <inheritdoc />
    public unsafe IReadOnlyList<SymbolSearchResult> SearchSymbols(
        string query,
        string? kind = null,
        string? language = null,
        string? path = null,
        int limit = 20)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        var request = JsonSerializer.Serialize(new { query, kind, language, path, limit }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(request);
        fixed (byte* pointer = bytes)
        {
            var status = NativeAbi.SearchSymbols(
                _handle, pointer, (nuint)bytes.Length, out var result, out var error);
            if (status != 0)
            {
                throw CreateException(error, "Native symbol search failed.");
            }
            try
            {
                return JsonSerializer.Deserialize<SearchSymbolsResponse>(ReadBuffer(result), JsonOptions)?.Results
                    ?? throw new ShioriEngineException("Native engine returned an invalid symbol search response.");
            }
            finally
            {
                NativeAbi.FreeBuffer(result);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _handle.Dispose();
        _disposed = true;
    }

    private static ShioriEngineException CreateException(
        NativeAbi.NativeBuffer error,
        string fallbackMessage)
    {
        try
        {
            var message = ReadBuffer(error);
            return new ShioriEngineException(string.IsNullOrWhiteSpace(message) ? fallbackMessage : message);
        }
        finally
        {
            NativeAbi.FreeBuffer(error);
        }
    }

    private IndexStatus ReadIndexStatus(IndexOperation operation, string fallbackMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var status = operation(_handle, out var result, out var error);
        if (status != 0)
        {
            throw CreateException(error, fallbackMessage);
        }

        try
        {
            return JsonSerializer.Deserialize<IndexStatus>(ReadBuffer(result), JsonOptions)
                ?? throw new ShioriEngineException("Native engine returned invalid index status.");
        }
        finally
        {
            NativeAbi.FreeBuffer(result);
        }
    }

    private static string ReadBuffer(NativeAbi.NativeBuffer buffer)
    {
        return buffer.Pointer == 0 || buffer.Length == 0
            ? string.Empty
            : Marshal.PtrToStringUTF8(buffer.Pointer, checked((int)buffer.Length));
    }

    private sealed record SearchResponse(IReadOnlyList<SearchResult> Results);

    private delegate int IndexOperation(
        ShioriEngineHandle handle,
        out NativeAbi.NativeBuffer result,
        out NativeAbi.NativeBuffer error);
}
