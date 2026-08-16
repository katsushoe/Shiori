using System.Net;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Shiori.Core.Lsp;

namespace Shiori.Cli.Server;

internal static class ShioriHttpServer
{
    private const string TokenVariable = "SHIORI_MCP_TOKEN";
    private const string WorkspacesVariable = "SHIORI_ALLOWED_WORKSPACES";

    internal static async Task<int> RunAsync(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var token = Environment.GetEnvironmentVariable(TokenVariable);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        {
            throw new InvalidOperationException($"{TokenVariable} must contain at least 32 characters.");
        }

        var allowedWorkspaces = ParseAllowedWorkspaces();

        var options = new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(ShioriHttpServer).Assembly.FullName,
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
        builder.Services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<NativeEngineRegistry>>();
            return new NativeEngineRegistry(
                allowedWorkspaces,
                (workspace, exception) => logger.LogError(
                    exception,
                    "Incremental index watcher failed for {Workspace}",
                    workspace));
        });
        builder.Services.AddSingleton<ILspServerConnectionFactory, ProcessLspServerConnectionFactory>();
        builder.Services.AddSingleton<LspServerManager>();
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

    private static string[] ParseAllowedWorkspaces()
    {
        var value = Environment.GetEnvironmentVariable(WorkspacesVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{WorkspacesVariable} must contain at least one workspace.");
        }

        var workspaces = value.Split(Path.PathSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var workspace in workspaces)
        {
            if (!Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace))
            {
                throw new InvalidOperationException($"Allowed workspace is unavailable: {workspace}");
            }
        }

        return workspaces;
    }
}
