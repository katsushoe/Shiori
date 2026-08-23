using System.Text.Json;
using Shiori.Cli;
using Shiori.Cli.Server;
using Shiori.Core.Engine;
using Shiori.Core.Integration;
using Shiori.Native;

ApplicationCulture.Apply();
InstallationLayout.ApplyDataDirectory();
return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "help" or "--help" or "-h")
    {
        Console.WriteLine(CliText.Get("Usage"));
        return 0;
    }

    try
    {
        return arguments[0] switch
        {
            "version" => RunVersion(),
            "find" => await RunFindAsync(arguments[1..]).ConfigureAwait(false),
            "index" => await RunIndexAsync(arguments[1..]).ConfigureAwait(false),
            "config" => RunConfig(arguments[1..]),
            "workspace" => await RunWorkspaceAsync(arguments[1..]).ConfigureAwait(false),
            "doctor" => await DoctorRunner.RunAsync().ConfigureAwait(false),
            "serve" => await RunServerAsync(arguments[1..]).ConfigureAwait(false),
            _ => Fail(CliText.Format("UnknownCommand", arguments[0])),
        };
    }
    catch (Exception exception)
    {
        return Fail(exception.Message);
    }
}

static int RunVersion()
{
    WriteJson(ShioriTools.GetVersion());
    return 0;
}

static int RunConfig(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return Fail(CliText.Get("ConfigTargetRequired"));
    }
    var port = int.TryParse(GetOption(arguments, "--port"), out var parsed) ? parsed : 39473;
    var name = GetOption(arguments, "--name") ?? "shiori";
    var configuration = arguments[0] switch
    {
        "claude" => ClaudeCodeConfigGenerator.Generate(port, name),
        "codex" => CodexConfigGenerator.Generate(port, name),
        _ => throw new ArgumentException(CliText.Format("UnknownConfigTarget", arguments[0])),
    };
    Console.WriteLine(configuration);
    return 0;
}

static async Task<int> RunWorkspaceAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return Fail(CliText.Get("WorkspaceCommandRequired"));
    }
    var registry = new WorkspaceRegistry();
    if (arguments[0] == "add" && arguments.Length >= 2)
    {
        var workspace = await registry.AddAsync(arguments[1]).ConfigureAwait(false);
        await RunIndexAsync(["rebuild", "--allow", workspace.Path]).ConfigureAwait(false);
        WriteJson(workspace);
        return 0;
    }

    object response = arguments[0] switch
    {
        "list" => await registry.ListAsync().ConfigureAwait(false),
        "remove" when arguments.Length >= 2 => await registry.RemoveAsync(arguments[1]).ConfigureAwait(false),
        "add" => throw new ArgumentException(CliText.Get("WorkspaceAddPathRequired")),
        "remove" => throw new ArgumentException(CliText.Get("WorkspaceRemoveTargetRequired")),
        _ => throw new ArgumentException(CliText.Format("UnknownWorkspaceCommand", arguments[0])),
    };
    WriteJson(response);
    return 0;
}

static async Task<int> RunIndexAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return Fail(CliText.Get("IndexCommandRequired"));
    }
    var requestedWorkspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var workspace = await RequireRegisteredWorkspaceAsync(requestedWorkspace).ConfigureAwait(false);
    using var engine = NativeShioriEngine.Open(workspace);
    if (arguments[0] == "status")
    {
        WriteJson(engine.GetIndexStatus());
        return 0;
    }
    if (arguments[0] is not ("build" or "rebuild"))
    {
        throw new ArgumentException(CliText.Format("UnknownIndexCommand", arguments[0]));
    }

    Console.WriteLine(CliText.Format("IndexStart", workspace));
    var totalDirectories = engine.CountIndexDirectories();
    IndexStatus status;
    try
    {
        status = engine.BuildIndex(totalDirectories, WriteProgress);
    }
    catch
    {
        if (!Console.IsOutputRedirected)
        {
            Console.WriteLine();
        }
        throw;
    }
    Console.WriteLine(CliText.Format("IndexComplete", workspace, status.IndexedFiles));
    return 0;
}

static void WriteProgress(IndexProgress progress)
{
    Console.WriteLine(CliText.Format(
        "IndexProgress",
        progress.Percent,
        IndexPathFormatter.FormatAbsolute(progress.Path)));
}

static async Task<int> RunFindAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return Fail(CliText.Get("FindQueryRequired"));
    }
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsed) ? parsed : 20;
    var registered = await new WorkspaceRegistry().ListAsync().ConfigureAwait(false);
    using var engines = new NativeEngineRegistry(registered.Select(workspace => workspace.Path));
    var coordinator = new WorkspaceCoordinator(engines);
    var requested = GetOptions(arguments, "--allow");
    var response = await coordinator
        .SearchFilesAsync(arguments[0], requested.Count == 0 ? null : requested, limit, CancellationToken.None)
        .ConfigureAwait(false);
    WriteJson(response);
    return 0;
}

static Task<int> RunServerAsync(string[] arguments)
{
    var port = int.TryParse(GetOption(arguments, "--port"), out var parsed) ? parsed : 39473;
    return ShioriHttpServer.RunAsync(port);
}

static async Task<string> RequireRegisteredWorkspaceAsync(string path)
{
    var requestedPath = Path.GetFullPath(path);
    var workspaces = await new WorkspaceRegistry().ListAsync().ConfigureAwait(false);
    var workspace = workspaces.FirstOrDefault(item =>
        string.Equals(
            Path.GetFullPath(item.Path),
            requestedPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    return workspace?.Path
        ?? throw new UnauthorizedAccessException($"Workspace is not registered: {path}");
}

static string? GetOption(string[] arguments, string option)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], option, StringComparison.Ordinal))
        {
            return arguments[index + 1];
        }
    }
    return null;
}

static IReadOnlyList<string> GetOptions(string[] arguments, string option)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], option, StringComparison.Ordinal))
        {
            values.Add(arguments[index + 1]);
        }
    }
    return values;
}

static void WriteJson<T>(T value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
}

static int Fail(string message)
{
    Console.Error.WriteLine(CliText.Format("Error", message));
    return 1;
}
