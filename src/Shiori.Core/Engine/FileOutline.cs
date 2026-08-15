namespace Shiori.Core.Engine;

/// <summary>Describes the indexed structure of one source file.</summary>
public sealed record FileOutline(
    string Path,
    string? Language,
    IReadOnlyList<OutlineSymbol> Symbols);

/// <summary>Describes one symbol and its nested members in a file outline.</summary>
public sealed record OutlineSymbol(
    string Name,
    string QualifiedName,
    string Kind,
    string Language,
    long StartLine,
    long StartColumn,
    long EndLine,
    long EndColumn,
    string? Signature,
    IReadOnlyList<OutlineSymbol> Children);
