using System.Security.Cryptography;
using System.Text;
using ContextBridge.Infrastructure.Security;

namespace ContextBridge.Service.Http;

public sealed class BearerTokenMiddleware(RequestDelegate next, TokenStore tokenStore)
{
    private const string HealthPath = "/health";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var header))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var headerValue = header.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var provided = headerValue["Bearer ".Length..].Trim();
        var stored = await tokenStore.GetOrCreateTokenAsync(context.RequestAborted);

        if (!FixedTimeEqual(provided, stored))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    // Timing-safe comparison to prevent token oracle attacks
    private static bool FixedTimeEqual(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
