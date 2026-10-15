using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Every 5 minutes, asks Vault's unauthenticated health endpoint whether it is usable (go-live
/// blocker C-8). Production Vault is single-node and manually unsealed (ADR-0002): after any pod
/// restart it re-seals, and every POK card payment fails with 503 until someone unseals it. Without
/// this check a sealed Vault would go unnoticed until a client tried to pay. A failure logs one
/// "VaultHealthCheckFailed" line; the Grafana rule vault-unavailable (grafana-alerting-configmap.yaml)
/// emails ops@ on it. What to do then: docs/infra/vault-self-hosted-runbook.md.
/// Environments without Vault (staging: no Vault Address) skip the check.
/// </summary>
public class VaultHealthCheckJob(
    IHttpClientFactory httpClientFactory,
    IOptions<VaultOptions> options,
    ILogger<VaultHealthCheckJob> logger)
{
    public const string HttpClientName = "VaultHealth";

    public async Task RunAsync(CancellationToken ct = default)
    {
        string address = options.Value.Address;
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        HttpClient http = httpClientFactory.CreateClient(HttpClientName);
        string state;
        int? statusCode = null;
        try
        {
            // standbyok: a standby node isn't an outage. Vault answers 200 unsealed+active,
            // 503 sealed, 501 not initialised — all without a token.
            using HttpResponseMessage response = await http.GetAsync(
                $"{address.TrimEnd('/')}/v1/sys/health?standbyok=true", ct);
            statusCode = (int)response.StatusCode;
            state = response.StatusCode switch
            {
                HttpStatusCode.OK => "ok",
                HttpStatusCode.ServiceUnavailable => "sealed",
                HttpStatusCode.NotImplemented => "not initialised",
                _ => "unhealthy",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Includes an HttpClient timeout (a TaskCanceledException the caller didn't ask for).
            state = $"unreachable ({ex.GetType().Name})";
        }

        if (state == "ok")
        {
            return;
        }

        logger.LogError(
            "VaultHealthCheckFailed: Vault is {VaultState} (HTTP {VaultStatusCode}); POK card payments return 503 until it is fixed — see docs/infra/vault-self-hosted-runbook.md.",
            state, statusCode);
    }
}
