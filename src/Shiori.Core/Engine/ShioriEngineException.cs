namespace Shiori.Core.Engine;

/// <summary>Represents a native search engine failure.</summary>
public sealed class ShioriEngineException : Exception
{
    /// <summary>Initializes a new engine exception.</summary>
    public ShioriEngineException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new engine exception with its underlying cause.</summary>
    public ShioriEngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
