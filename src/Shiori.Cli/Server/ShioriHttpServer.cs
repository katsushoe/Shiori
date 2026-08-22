using System.Net;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Shiori.Cli;
using Shiori.Core.Logging;

namespace Shiori.Cli.Server;

internal static class ShioriHttpServer
{
    private const string TokenVariable = "SHIORI_MCP_TOKEN";
    internal static async Task<int> RunAsync(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var token = Environment.GetEnvironmentVariable(TokenVariable);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        {
            throw new InvalidOperationException($"{TokenVariable} must contain at least 32 characters.");
        }

        var registeredWorkspaces = await new WorkspaceRegistry().ListAsync().ConfigureAwait(false);
        var allowedWorkspaces = ValidateRegisteredWorkspaces(registeredWorkspaces);

        var options = new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(ShioriHttpServer).Assembly.FullName,
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
        builder.Logging.AddProvider(new FileLoggerProvider(InstallationLayout.GetDirectory("logs")));
        builder.Services.AddSingleton(_ => new NativeEngineRegistry(allowedWorkspaces));
        builder.Services.AddSingleton<IWorkspaceEngineProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<NativeEngineRegistry>());
        builder.Services.AddSingleton<WorkspaceCoordinator>();
        builder.Services.AddSingleton<IIndexTerminalLauncher, WindowsTerminalIndexLauncher>();
        builder.Services.AddHostedService<InterruptedIndexResumeService>();
        builder.Services.AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<ShioriTools>();

        var app = builder.Build();
        _ = app.Services.GetRequiredService<NativeEngineRegistry>();
        app.UseHostFiltering();
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/mcp"),
            branch =>
            {
                branch.UseMiddleware<BearerTokenMiddleware>(token);
                branch.Use(ValidateOriginAsync);
            });
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapMcp("/mcp");
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task ValidateOriginAsync(HttpContext context, RequestDelegate next)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !IsLoopbackOrigin(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsLoopbackOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string[] ValidateRegisteredWorkspaces(IReadOnlyList<Shiori.Core.Engine.WorkspaceInfo> workspaces)
    {
        foreach (var workspace in workspaces)
        {
            if (!Path.IsPathFullyQualified(workspace.Path) || !Directory.Exists(workspace.Path))
            {
                throw new InvalidOperationException($"Registered workspace is unavailable: {workspace.Path}");
            }
        }

        return workspaces.Select(workspace => workspace.Path).ToArray();
    }
}
