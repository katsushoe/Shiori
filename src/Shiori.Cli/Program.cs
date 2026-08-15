using System.Text.Json;
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
            "doctor" => RunDoctor(),
            _ => Fail($"Unknown command: {arguments[0]}")
        };
    }
    catch (Exception exception)
    {
        return Fail(exception.Message);
    }
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

static string? GetOption(string[] arguments, string option)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            return arguments[index + 1];
    }
    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static string Usage() => """
    Usage:
      shiori find <query> --allow <directory> [--limit <1-100>]
      shiori doctor
    """;
