using Shiori.Core.Engine;

namespace Shiori.Cli.Server;

/// <summary>Resolves explicitly allowed workspace engines for coordinated operations.</summary>
public interface IWorkspaceEngineProvider
{
    /// <summary>Resolves requested paths, or all allowed paths when none are requested.</summary>
    IReadOnlyList<string> ResolveWorkspacePaths(IReadOnlyList<string>? requested);

    /// <summary>Gets or lazily opens the engine for an allowed workspace.</summary>
    IShioriEngine GetEngine(string workspace);
}
