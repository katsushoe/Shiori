using System.Runtime.InteropServices;
using System.Text.Json;

namespace Shiori.Native;

/// <summary>Exposes native engine diagnostics.</summary>
public static class NativeAbiStatus
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Gets the loaded native engine ABI version.</summary>
    public static uint GetAbiVersion() => NativeAbi.GetAbiVersion();

    /// <summary>Runs native engine, SQLite, and ripgrep diagnostics.</summary>
    public static NativeDiagnostics GetDiagnostics()
    {
        var status = NativeAbi.GetDiagnostics(out var result, out var error);
        if (status != 0)
        {
            try
            {
                var message = ReadBuffer(error);
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(message) ? "Native diagnostics failed." : message);
            }
            finally
            {
                NativeAbi.FreeBuffer(error);
            }
        }

        try
        {
            return JsonSerializer.Deserialize<NativeDiagnostics>(ReadBuffer(result), JsonOptions)
                ?? throw new InvalidOperationException("Native diagnostics returned an invalid response.");
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
}
