using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;

namespace Pena_e_Arte.API.Extensions;

// /hangfire is reached by a plain browser navigation, which never carries the SPA's JWT
// (stored in local/session storage, only ever attached as an Authorization: Bearer header by
// fetchBaseQuery — never sent on a top-level navigation, and there is no cookie-auth scheme
// registered). So the original IsInRole("issuer") check alone left this dashboard completely
// unreachable by its intended operators, while docker-compose.yml's required
// Hangfire:DashboardUsername/Password sat unread — enforced config with no effect. HTTP Basic
// Auth is now the real gate; the issuer-JWT check remains as an additional layer for the case
// where a valid bearer token happens to be attached (e.g. a scripted curl request).
public class HangfireDashboardAuthFilter(IConfiguration configuration) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        HttpContext httpContext = context.GetHttpContext();

        if (httpContext.User.Identity?.IsAuthenticated == true && httpContext.User.IsInRole("issuer"))
            return true;

        return AuthorizeBasic(httpContext);
    }

    private bool AuthorizeBasic(HttpContext httpContext)
    {
        string expectedUsername = configuration["Hangfire:DashboardUsername"] ?? string.Empty;
        string expectedPassword = configuration["Hangfire:DashboardPassword"] ?? string.Empty;

        // Unset/empty config (e.g. a deployment path that never set the required env vars) must
        // fail closed, not grant access on blank credentials.
        if (string.IsNullOrEmpty(expectedUsername) || string.IsNullOrEmpty(expectedPassword))
            return false;

        string? authorizationHeader = httpContext.Request.Headers.Authorization;
        if (TryParseBasicCredentials(authorizationHeader, out string username, out string password) &&
            FixedTimeEquals(username, expectedUsername) &&
            FixedTimeEquals(password, expectedPassword))
        {
            return true;
        }

        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\"";
        return false;
    }

    private static bool TryParseBasicCredentials(
        string? authorizationHeader, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith("Basic ", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(authorizationHeader["Basic ".Length..]));
            int separatorIndex = decoded.IndexOf(':');
            if (separatorIndex <= 0) return false;

            username = decoded[..separatorIndex];
            password = decoded[(separatorIndex + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
