using System.Text.Json;
using Shiori.Cli.Server;
using Shiori.Native;

return Run(args);

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
            "find" => RunFind(arguments[1..]),
            "grep" => RunGrep(arguments[1..]),
            "index" => RunIndex(arguments[1..]),
            "doctor" => RunDoctor(),
            "serve" => RunServer(arguments[1..]),
            _ => Fail($"Unknown command: {arguments[0]}")
        };
    }
    catch (Exception exception)
    {
        return Fail(exception.Message);
    }
}

static int RunIndex(string[] arguments)
{
    if (arguments.Length == 0) return Fail("index requires build, status, or rebuild.");
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException("--allow is required.");
    using var engine = NativeShioriEngine.Open(workspace);
    var status = arguments[0] switch
    {
        "build" => engine.BuildIndex(),
        "status" => engine.GetIndexStatus(),
        "rebuild" => engine.RebuildIndex(),
        _ => throw new ArgumentException($"Unknown index command: {arguments[0]}")
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
    if (arguments.Length == 0) return Fail("grep requires a query.");
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException("--allow is required.");
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
    if (arguments.Length == 0) return Fail("find requires a query.");
    var workspace = GetOption(arguments, "--allow")
        ?? throw new ArgumentException("--allow is required.");
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

static int RunDoctor()
{
    var version = NativeAbiStatus.GetAbiVersion();
    Console.WriteLine($"native_engine: available (ABI {version})");
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
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static string Usage() => """
    Usage:
      shiori find <query> --allow <directory> [--limit <1-100>]
      shiori grep <query> --allow <directory> [--path <path>] [--glob <glob>]
        [--regex] [--case-sensitive] [--context <0-10>] [--limit <1-100>]
      shiori index build --allow <directory>
      shiori index status --allow <directory>
      shiori index rebuild --allow <directory>
      shiori doctor
      shiori serve [--port <1-65535>]
    """;
