namespace Shiori.Core.Logging;

/// <summary>Marks a log state that carries the caller's source location.</summary>
internal interface ISourceLocatedLogState
{
    string SourceFile { get; }

    int SourceLine { get; }
}

/// <summary>A pre-formatted search log message paired with its caller source location.</summary>
internal sealed record SearchLogState(string Message, string SourceFile, int SourceLine) : ISourceLocatedLogState
{
    public override string ToString() => Message;
}
