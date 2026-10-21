using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Every 5 minutes, checks that the app can really use Vault (go-live blocker C-8). Production Vault
/// is single-node and manually unsealed (ADR-0002): after any pod restart it re-seals, and every POK
/// card payment fails with 503 until someone unseals it. Without this check a broken Vault would go
/// unnoticed until a client tried to pay. A failure logs one "VaultHealthCheckFailed" line; the
/// Grafana rule vault-unavailable (grafana-alerting-configmap.yaml) emails ops@ on it. What to do
/// then: docs/infra/vault-self-hosted-runbook.md.
///
/// Three checks, in order:
/// 1. Vault itself: the unauthenticated health endpoint (sealed, not initialised, unreachable).
/// 2. The app's own token: a periodic token expires when nobody renews it within its period — the
///    production token did exactly that in October 2026 while the health endpoint still said "ok".
///    Here it is looked up, and renewed once less than half of its period is left.
/// 3. The token's permissions on the studios' POK key paths: connecting POK writes them (KV v2
///    patch, or create on a first connect) and every payment reads them. Asked with
///    capabilities-self, so nothing is written.
/// Environments without Vault (staging: no Vault Address) skip the check.
/// </summary>
public class VaultHealthCheckJob(
    IHttpClientFactory httpClientFactory,
    IOptions<VaultOptions> options,
    ILogger<VaultHealthCheckJob> logger)
{
    public const string HttpClientName = "VaultHealth";

    // Same path shape as ConnectPokAccountCommand's studios/{studioId}/pok, for a studio that
    // doesn't exist — the policy is matched on the path, so any studio id answers the same.
    internal const string ProbeSecretPath = "studios/00000000-0000-0000-0000-000000000000/pok";

    // read: every payment; patch: re-connect; create + update: a studio's first connect.
    internal static readonly string[] RequiredCapabilities = ["read", "create", "update", "patch"];

    public async Task RunAsync(CancellationToken ct = default)
    {
        VaultOptions opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Address))
        {
            return;
        }

        string baseUrl = opts.Address.TrimEnd('/');
        HttpClient http = httpClientFactory.CreateClient(HttpClientName);
        (string state, int? statusCode) result;
        try
        {
            result = await CheckServerAsync(http, baseUrl, ct);
            if (result.state == "ok" && !string.IsNullOrWhiteSpace(opts.Token))
            {
                result = await CheckTokenAsync(http, baseUrl, opts, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Includes an HttpClient timeout (a TaskCanceledException the caller didn't ask for).
            result = ($"unreachable ({ex.GetType().Name})", null);
        }

        if (result.state == "ok")
        {
            return;
        }

        logger.LogError(
            "VaultHealthCheckFailed: Vault is {VaultState} (HTTP {VaultStatusCode}); POK card payments return 503 until it is fixed — see docs/infra/vault-self-hosted-runbook.md.",
            result.state, result.statusCode);
    }

    private static async Task<(string, int?)> CheckServerAsync(HttpClient http, string baseUrl, CancellationToken ct)
    {
        // standbyok: a standby node isn't an outage. Vault answers 200 unsealed+active,
        // 503 sealed, 501 not initialised — all without a token.
        using HttpResponseMessage response = await http.GetAsync($"{baseUrl}/v1/sys/health?standbyok=true", ct);
        string state = response.StatusCode switch
        {
            HttpStatusCode.OK => "ok",
            HttpStatusCode.ServiceUnavailable => "sealed",
            HttpStatusCode.NotImplemented => "not initialised",
            _ => "unhealthy",
        };
        return (state, (int)response.StatusCode);
    }

    private async Task<(string, int?)> CheckTokenAsync(HttpClient http, string baseUrl, VaultOptions opts, CancellationToken ct)
    {
        using (HttpRequestMessage lookup = TokenRequest(HttpMethod.Get, $"{baseUrl}/v1/auth/token/lookup-self", opts.Token))
        using (HttpResponseMessage response = await http.SendAsync(lookup, ct))
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return ("rejecting the app's token (invalid or expired)", (int)response.StatusCode);
            }
            if (!response.IsSuccessStatusCode)
            {
                return ("not answering the token lookup", (int)response.StatusCode);
            }

            using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            JsonElement data = body.RootElement.GetProperty("data");
            long ttl = data.TryGetProperty("ttl", out JsonElement t) ? t.GetInt64() : 0;
            long period = data.TryGetProperty("period", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;
            bool renewable = data.TryGetProperty("renewable", out JsonElement r) && r.ValueKind == JsonValueKind.True;

            // ttl 0 = a token that never expires (the local dev root token): nothing to renew.
            if (renewable && period > 0 && ttl > 0 && ttl < period / 2)
            {
                using HttpRequestMessage renew = TokenRequest(HttpMethod.Post, $"{baseUrl}/v1/auth/token/renew-self", opts.Token);
                renew.Content = JsonContent.Create(new { });
                using HttpResponseMessage renewed = await http.SendAsync(renew, ct);
                if (!renewed.IsSuccessStatusCode)
                {
                    return ("refusing to renew the app's token", (int)renewed.StatusCode);
                }
                logger.LogInformation(
                    "VaultTokenRenewed: the app's Vault token had {TtlHoursLeft} h left and was renewed for another {PeriodHours} h.",
                    ttl / 3600, period / 3600);
            }
        }

        string probePath = $"{opts.MountPoint}/data/{ProbeSecretPath}";
        using HttpRequestMessage capabilities = TokenRequest(HttpMethod.Post, $"{baseUrl}/v1/sys/capabilities-self", opts.Token);
        capabilities.Content = JsonContent.Create(new { paths = new[] { probePath } });
        using HttpResponseMessage answer = await http.SendAsync(capabilities, ct);
        if (!answer.IsSuccessStatusCode)
        {
            return ("not answering the permission check", (int)answer.StatusCode);
        }

        using JsonDocument caps = await JsonDocument.ParseAsync(await answer.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        // Vault puts the answer both at the top level and under "data", keyed by the path.
        JsonElement root = caps.RootElement;
        bool found = root.TryGetProperty(probePath, out JsonElement list)
            || (root.TryGetProperty("data", out JsonElement d) && d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty(probePath, out list));
        HashSet<string> granted = found && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(c => c.GetString() ?? "").ToHashSet()
            : [];
        if (granted.Contains("root"))
        {
            return ("ok", null);
        }
        string[] missing = RequiredCapabilities.Where(c => !granted.Contains(c)).ToArray();
        return missing.Length == 0
            ? ("ok", null)
            : ($"missing permissions on the studios' POK keys ({string.Join(", ", missing)})", null);
    }

    private static HttpRequestMessage TokenRequest(HttpMethod method, string url, string token)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.Add("X-Vault-Token", token);
        return request;
    }
}
