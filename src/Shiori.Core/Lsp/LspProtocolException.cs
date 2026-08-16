namespace Shiori.Core.Lsp;

/// <summary>Represents an invalid frame or an error response from a language server.</summary>
public sealed class LspProtocolException : Exception
{
    /// <summary>Initializes a protocol exception.</summary>
    public LspProtocolException(string message)
        : base(message)
    {
    }
}
