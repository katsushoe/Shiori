using System.Text.Json;
using Shiori.Cli;
using Shiori.Cli.Server;
using Shiori.Core.Engine;
using Shiori.Core.Integration;
using Shiori.Core.Lsp;
using Shiori.Core.Search;
using Shiori.Native;

try
{
    ApplicationCulture.Apply();
    return Run(args);
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    return Fail(exception.Message);
}

static int Run(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "help" or "--help" or "-h")
    {
        Console.WriteLine(Usage());
        return 0;
    }

    try
    {
        return arguments[0] switch
        {
            "ast" => RunAst(arguments[1..]),
            "find" => RunFind(arguments[1..]),
            "grep" => RunGrep(arguments[1..]),
            "index" => RunIndex(arguments[1..]),
            "navigate" => RunNavigate(arguments[1..]),
            "outline" => RunOutline(arguments[1..]),
            "search" => RunSearch(arguments[1..]),
            "symbol" => RunSymbol(arguments[1..]),
            "config" => RunConfig(arguments[1..]),
            "workspace" => RunWorkspace(arguments[1..]),
            "doctor" => DoctorRunner.Run(),
            "serve" => RunServer(arguments[1..]),
            _ => Fail(CliText.Format("UnknownCommand", arguments[0]))
        };
    }
    catch (Exception exception)
    {
        return Fail(exception.Message);
    }
}

static int RunAst(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("AstQueryRequired"));
    var language = GetOption(arguments, "--language")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--language"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsed) ? parsed : 20;
    using var engine = NativeShioriEngine.Open(workspace);
    var response = new AstSearchResponse(
        engine.SearchAst(language, arguments[0], GetOption(arguments, "--path"), limit));
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunNavigate(string[] arguments)
{
    if (arguments.Length < 2
        || arguments[0] is not ("definition" or "references" or "implementations" or "callers" or "callees"))
        return Fail(CliText.Get("NavigateRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var line = int.TryParse(GetOption(arguments, "--line"), out var parsedLine) ? parsedLine : 0;
    var column = int.TryParse(GetOption(arguments, "--column"), out var parsedColumn) ? parsedColumn : 0;
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsedLimit) ? parsedLimit : 20;
    var descriptor = CSharpLanguageServerDiscovery.Find();
    var manager = new LspServerManager(new ProcessLspServerConnectionFactory());
    try
    {
        var response = LspNavigationService.NavigateAsync(
            manager, workspace, arguments[1], line, column, arguments[0], limit, descriptor)
            .GetAwaiter().GetResult();
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        }));
        return response.Success ? 0 : 1;
    }
    finally
    {
        manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

static int RunConfig(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("ConfigTargetRequired"));
    var port = int.TryParse(GetOption(arguments, "--port"), out var parsed) ? parsed : 39473;
    var name = GetOption(arguments, "--name") ?? "shiori";
    var configuration = arguments[0] switch
    {
        "claude" => ClaudeCodeConfigGenerator.Generate(port, name),
        "codex" => CodexConfigGenerator.Generate(port, name),
        _ => throw new ArgumentException(CliText.Format("UnknownConfigTarget", arguments[0]))
    };
    Console.WriteLine(configuration);
    return 0;
}

static int RunSearch(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("SearchQueryRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsed) ? parsed : 20;
    using var engine = NativeShioriEngine.Open(workspace);
    var response = UnifiedSearchService.SearchAsync(
        engine, arguments[0], GetOption(arguments, "--path"), limit).GetAwaiter().GetResult();
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunSymbol(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("SymbolQueryRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsed) ? parsed : 20;
    using var engine = NativeShioriEngine.Open(workspace);
    var response = new SearchSymbolsResponse(engine.SearchSymbols(
        arguments[0], GetOption(arguments, "--kind"), GetOption(arguments, "--language"),
        GetOption(arguments, "--path"), limit));
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunOutline(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("OutlinePathRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    using var engine = NativeShioriEngine.Open(workspace);
    var outline = engine.GetFileOutline(arguments[0]);
    Console.WriteLine(JsonSerializer.Serialize(outline, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunWorkspace(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("WorkspaceCommandRequired"));
    var registry = new WorkspaceRegistry();
    object response = arguments[0] switch
    {
        "add" when arguments.Length >= 2 => registry.Add(arguments[1]),
        "list" => new { workspaces = registry.List() },
        "remove" when arguments.Length >= 2 => registry.Remove(arguments[1]),
        "add" => throw new ArgumentException(CliText.Get("WorkspaceAddPathRequired")),
        "remove" => throw new ArgumentException(CliText.Get("WorkspaceRemoveTargetRequired")),
        _ => throw new ArgumentException(CliText.Format("UnknownWorkspaceCommand", arguments[0]))
    };
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunIndex(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("IndexCommandRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    using var engine = NativeShioriEngine.Open(workspace);
    var status = arguments[0] switch
    {
        "build" => engine.BuildIndex(),
        "status" => engine.GetIndexStatus(),
        "rebuild" => engine.RebuildIndex(),
        _ => throw new ArgumentException(CliText.Format("UnknownIndexCommand", arguments[0]))
    };
    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunGrep(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("GrepQueryRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsedLimit) ? parsedLimit : 20;
    var context = int.TryParse(GetOption(arguments, "--context"), out var parsedContext) ? parsedContext : 0;
    using var engine = NativeShioriEngine.Open(workspace);
    var response = new
    {
        results = engine.SearchText(
            arguments[0],
            GetOption(arguments, "--path"),
            GetOption(arguments, "--glob"),
            HasOption(arguments, "--regex"),
            HasOption(arguments, "--case-sensitive"),
            context,
            limit),
    };
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunFind(string[] arguments)
{
    if (arguments.Length == 0) return Fail(CliText.Get("FindQueryRequired"));
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException(CliText.Format("OptionRequired", "--allow"));
    var limit = int.TryParse(GetOption(arguments, "--limit"), out var parsed) ? parsed : 20;
    using var engine = NativeShioriEngine.Open(workspace);
    var response = new { results = engine.SearchFiles(arguments[0], limit) };
    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    }));
    return 0;
}

static int RunServer(string[] arguments)
{
    var port = int.TryParse(GetOption(arguments, "--port"), out var parsed) ? parsed : 39473;
    return ShioriHttpServer.RunAsync(port).GetAwaiter().GetResult();
}

static string? GetOption(string[] arguments, string option)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            return arguments[index + 1];
    }
    return null;
}

static bool HasOption(string[] arguments, string option) =>
    arguments.Contains(option, StringComparer.Ordinal);

static int Fail(string message)
{
    Console.Error.WriteLine(CliText.Format("Error", message));
    return 1;
}

static string Usage() => CliText.Get("Usage");
