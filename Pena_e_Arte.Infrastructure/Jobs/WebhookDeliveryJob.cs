using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Contracts.Responses.ExternalApi;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Delivers one webhook event to a studio's configured endpoint. Runs with no ambient
/// tenant scope (Hangfire jobs are not HTTP requests) — every query here is
/// IgnoreQueryFilters() with an explicit studioId predicate, same pattern as
/// ChatNotificationJob/ManualReminderJob. A non-2xx response or transport exception is
/// re-thrown after being logged, letting Hangfire's own AutomaticRetryAttribute (the
/// project default — no custom override) handle backoff/retry; this job only decides
/// WHETHER to retry, never how long to wait.
/// </summary>
public class WebhookDeliveryJob(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    ITokenEncryptor encryptor,
    ILogger<WebhookDeliveryJob> logger)
{
    private const int AutoDisableThreshold = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DeliverAsync(Guid studioId, string eventType, Guid resourceId, CancellationToken ct)
    {
        WebhookEndpoint? endpoint = await db.WebhookEndpoints.IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.StudioId == studioId && w.DeletedAt == null && w.IsActive, ct);
        if (endpoint is null) return; // no active endpoint configured — nothing to deliver, nothing to log

        object? data = eventType == "ping"
            ? new { message = "This is a test event from your webhook configuration." }
            : await BuildPayloadAsync(studioId, eventType, resourceId, ct);

        if (data is null)
        {
            logger.LogWarning(
                "WebhookDeliveryJob: resource {ResourceId} for event {EventType} not found, skipping",
                resourceId, eventType);
            return;
        }

        Guid deliveryId = Guid.NewGuid();
        string body = JsonSerializer.Serialize(
            new { id = deliveryId, type = eventType, createdAt = DateTime.UtcNow, data }, JsonOptions);

        if (!await WebhookUrlValidator.IsAllowedAsync(endpoint.Url, ct))
        {
            await RecordAttemptAsync(endpoint, eventType, resourceId, null, false,
                "URL failed delivery-time validation.", ct);
            return; // the URL itself is unsafe/invalid — retrying changes nothing, so don't
        }

        string secret = encryptor.Decrypt(endpoint.EncryptedSecret);
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signature = WebhookSigner.Sign(secret, timestamp, body);

        using HttpClient client = httpFactory.CreateClient("Webhooks");
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Webhook-Id", deliveryId.ToString());
        request.Headers.Add("X-Webhook-Timestamp", timestamp);
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, ct);
            int statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                await RecordAttemptAsync(endpoint, eventType, resourceId, statusCode, true, null, ct);
            }
            else
            {
                await RecordAttemptAsync(endpoint, eventType, resourceId, statusCode, false,
                    $"Endpoint responded with {statusCode}.", ct);
                throw new InvalidOperationException($"Webhook delivery got HTTP {statusCode}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAttemptAsync(endpoint, eventType, resourceId, null, false, ex.Message, ct);
            throw; // transient transport failure — let Hangfire retry
        }
    }

    private async Task<object?> BuildPayloadAsync(
        Guid studioId, string eventType, Guid resourceId, CancellationToken ct)
    {
        if (eventType.StartsWith("appointment.", StringComparison.Ordinal))
        {
            Appointment? a = await db.Appointments.IgnoreQueryFilters()
                .Include(x => x.Client)
                .Include(x => x.Artist)
                .FirstOrDefaultAsync(x => x.StudioId == studioId && x.Id == resourceId, ct);

            return a is null ? null : new ExternalAppointmentResponse(
                a.Id, a.Date, a.DurationMinutes, a.Status.ToString(),
                a.Client.FirstName + " " + a.Client.LastName,
                a.Artist != null ? a.Artist.FirstName + " " + a.Artist.LastName : null,
                a.DepositAmount, a.DepositStatus.ToString(), a.CreatedAt);
        }

        if (eventType.StartsWith("client.", StringComparison.Ordinal))
        {
            Client? c = await db.Clients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.StudioId == studioId && x.DeletedAt == null && x.Id == resourceId, ct);

            return c is null ? null : new ExternalClientResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt);
        }

        return null;
    }

    private async Task RecordAttemptAsync(
        WebhookEndpoint endpoint, string eventType, Guid resourceId,
        int? statusCode, bool succeeded, string? errorMessage, CancellationToken ct)
    {
        db.WebhookDeliveries.Add(new WebhookDelivery
        {
            StudioId = endpoint.StudioId,
            WebhookEndpointId = endpoint.Id,
            EventType = eventType,
            ResourceId = resourceId,
            ResponseStatusCode = statusCode,
            Succeeded = succeeded,
            ErrorMessage = errorMessage,
        });

        endpoint.LastDeliveryAt = DateTime.UtcNow;
        endpoint.LastDeliverySucceeded = succeeded;
        endpoint.UpdatedAt = DateTime.UtcNow;

        if (succeeded)
        {
            endpoint.ConsecutiveFailureCount = 0;
        }
        else
        {
            endpoint.ConsecutiveFailureCount++;
            if (endpoint.ConsecutiveFailureCount >= AutoDisableThreshold)
            {
                endpoint.IsActive = false;
                logger.LogWarning(
                    "WebhookDeliveryJob: auto-disabled endpoint for studio {StudioId} after {Count} consecutive failures",
                    endpoint.StudioId, endpoint.ConsecutiveFailureCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
