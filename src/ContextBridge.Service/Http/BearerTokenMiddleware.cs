using System.Security.Cryptography;
using System.Text;
using ContextBridge.Infrastructure.Security;

namespace ContextBridge.Service.Http;

public sealed class BearerTokenMiddleware(RequestDelegate next, TokenStore tokenStore)
{
    private const string HealthPath = "/health";
    private const string DashboardPath = "/dashboard";
    private const string DashboardApiPath = "/api/dashboard";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments(DashboardPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments(DashboardApiPath, StringComparison.OrdinalIgnoreCase))
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
