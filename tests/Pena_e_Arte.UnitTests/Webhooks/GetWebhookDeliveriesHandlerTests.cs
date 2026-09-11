using FluentAssertions;
using Pena_e_Arte.Application.Webhooks.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Webhooks;

public class GetWebhookDeliveriesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _endpointId = Guid.NewGuid();

    private GetWebhookDeliveriesHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ReturnsMostRecentFirst()
    {
        _db.WebhookDeliveries.AddRange(
            new WebhookDelivery
            {
                StudioId = _studioId,
                WebhookEndpointId = _endpointId,
                EventType = "appointment.created",
                Succeeded = true,
                AttemptedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new WebhookDelivery
            {
                StudioId = _studioId,
                WebhookEndpointId = _endpointId,
                EventType = "appointment.cancelled",
                Succeeded = false,
                AttemptedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            });
        await _db.SaveChangesAsync();

        List<WebhookDeliveryResponse> result =
            await CreateSut().Handle(new GetWebhookDeliveriesQuery(), default);

        result.Should().HaveCount(2);
        result[0].EventType.Should().Be("appointment.cancelled");
        result[1].EventType.Should().Be("appointment.created");
    }

    [Fact]
    public async Task Handle_PageSizeClampedAboveMax_StillReturnsAllAvailable()
    {
        for (int i = 0; i < 5; i++)
        {
            _db.WebhookDeliveries.Add(new WebhookDelivery
            {
                StudioId = _studioId,
                WebhookEndpointId = _endpointId,
                EventType = "ping",
                Succeeded = true,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-i),
            });
        }
        await _db.SaveChangesAsync();

        List<WebhookDeliveryResponse> result =
            await CreateSut().Handle(new GetWebhookDeliveriesQuery(PageSize: 500), default);

        result.Should().HaveCount(5);
    }
}
