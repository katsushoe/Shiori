using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Shiori.Cli;

/// <summary>Stores the MCP bearer token in a file protected by DPAPI for the current Windows user.</summary>
internal sealed class McpTokenStore
{
    internal const string FileName = "mcp-token.bin";
    private const int TokenByteCount = 32;
    private static readonly byte[] Entropy = "Shiori.McpToken.v1"u8.ToArray();

    /// <summary>Initializes a store in the installation config directory or an explicit directory.</summary>
    internal McpTokenStore(string? configDirectory = null)
    {
        FilePath = Path.Combine(configDirectory ?? InstallationLayout.GetDirectory("config"), FileName);
    }

    /// <summary>Gets the protected token file path.</summary>
    internal string FilePath { get; }

    /// <summary>Reads the token, or returns null when it has not been created.</summary>
    /// <exception cref="CryptographicException">The file cannot be decrypted by the current user.</exception>
    internal string? TryRead()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(FilePath)));
    }

    /// <summary>Reads the token, creating and persisting a random token when none exists.</summary>
    internal string GetOrCreate()
    {
        var existing = TryRead();
        if (existing is not null)
        {
            return existing;
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenByteCount));
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The MCP token directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temporaryPath, Protect(Encoding.UTF8.GetBytes(token)));
        try
        {
            File.Move(temporaryPath, FilePath, overwrite: false);
            return token;
        }
        catch (IOException) when (File.Exists(FilePath))
        {
            // Another Shiori process created the token first; both processes must share it.
            File.Delete(temporaryPath);
            return TryRead() ?? throw new InvalidOperationException("The MCP token file disappeared.");
        }
    }

    private static byte[] Protect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The MCP token store requires Windows DPAPI.");
        }

        return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] value)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The MCP token store requires Windows DPAPI.");
        }

        return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
    }
}
