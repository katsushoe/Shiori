using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Shiori.Core.Engine;

namespace Shiori.Native;

/// <summary>Calls the Rust file-search engine through its versioned C ABI.</summary>
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

            return new NativeShioriEngine(new ShioriEngineHandle(nativeHandle), abiVersion);
        }
    }

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
    public ulong CountIndexDirectories()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var status = NativeAbi.CountIndexDirectories(_handle, out var count, out var error);
        if (status != 0)
        {
            throw CreateException(error, "Native directory count failed.");
        }

        return count;
    }

    /// <inheritdoc />
    public unsafe IndexStatus BuildIndex(
        ulong totalDirectories,
        Action<IndexProgress>? progress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(totalDirectories);
        var state = new ProgressCallbackState(progress);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var status = NativeAbi.BuildIndex(
                _handle,
                totalDirectories,
                &ReportProgress,
                GCHandle.ToIntPtr(stateHandle),
                out var result,
                out var error);
            if (state.Exception is not null)
            {
                if (status == 0)
                {
                    NativeAbi.FreeBuffer(result);
                }
                else
                {
                    NativeAbi.FreeBuffer(error);
                }
                throw new ShioriEngineException("Index progress reporting failed.", state.Exception);
            }
            if (status != 0)
            {
                throw CreateException(error, "Native index build failed.");
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
        finally
        {
            stateHandle.Free();
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
                return JsonSerializer.Deserialize<IReadOnlyList<SearchResult>>(ReadBuffer(result), JsonOptions)
                    ?? throw new ShioriEngineException("Native engine returned an invalid response.");
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
        if (_disposed)
        {
            return;
        }
        _handle.Dispose();
        _disposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ReportProgress(
        ulong completed,
        ulong total,
        byte* path,
        nuint pathLength,
        nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        if (handle.Target is not ProgressCallbackState state || state.Callback is null || state.Exception is not null)
        {
            return;
        }

        try
        {
            var relativePath = path is null || pathLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(path, checked((int)pathLength));
            state.Callback(new IndexProgress(completed, total, relativePath));
        }
        catch (Exception exception)
        {
            state.Exception = exception;
        }
    }

    private static ShioriEngineException CreateException(NativeAbi.NativeBuffer error, string fallbackMessage)
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

    private static string ReadBuffer(NativeAbi.NativeBuffer buffer) =>
        buffer.Pointer == 0 || buffer.Length == 0
            ? string.Empty
            : Marshal.PtrToStringUTF8(buffer.Pointer, checked((int)buffer.Length));

    private sealed class ProgressCallbackState(Action<IndexProgress>? callback)
    {
        internal Action<IndexProgress>? Callback { get; } = callback;
        internal Exception? Exception { get; set; }
    }

    private delegate int IndexOperation(
        ShioriEngineHandle handle,
        out NativeAbi.NativeBuffer result,
        out NativeAbi.NativeBuffer error);
}
