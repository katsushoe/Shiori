using System.Security.Cryptography;
using System.Text;

namespace Shiori.Cli.Server;

internal sealed class BearerTokenMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private readonly RequestDelegate _next;
    private readonly byte[] _expectedToken;

    public BearerTokenMiddleware(RequestDelegate next, string token)
    {
        _next = next;
        _expectedToken = Encoding.UTF8.GetBytes(token);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var suppliedToken = Encoding.UTF8.GetBytes(authorization[BearerPrefix.Length..]);
        if (suppliedToken.Length != _expectedToken.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedToken, _expectedToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
