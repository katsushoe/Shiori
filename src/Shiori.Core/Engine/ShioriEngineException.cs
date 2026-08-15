namespace Shiori.Core.Engine;

/// <summary>Represents a native search engine failure.</summary>
public sealed class ShioriEngineException : Exception
{
    /// <summary>Initializes a new engine exception.</summary>
    public ShioriEngineException(string message)
        : base(message)
    {
    }
}
