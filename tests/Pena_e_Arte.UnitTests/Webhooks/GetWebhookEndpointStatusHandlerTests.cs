using FluentAssertions;
using Pena_e_Arte.Application.Webhooks.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Webhooks;

public class GetWebhookEndpointStatusHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetWebhookEndpointStatusHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoEndpoint_ReturnsHasEndpointFalse()
    {
        WebhookEndpointStatusResponse result =
            await CreateSut().Handle(new GetWebhookEndpointStatusQuery(), default);

        result.HasEndpoint.Should().BeFalse();
        result.Url.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ExistingEndpoint_ReturnsItsStatus()
    {
        _db.WebhookEndpoints.Add(new WebhookEndpoint
        {
            StudioId = _studioId,
            Url = "https://example.com/hook",
            IsActive = true,
            LastDeliveryAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastDeliverySucceeded = true,
        });
        await _db.SaveChangesAsync();

        WebhookEndpointStatusResponse result =
            await CreateSut().Handle(new GetWebhookEndpointStatusQuery(), default);

        result.HasEndpoint.Should().BeTrue();
        result.Url.Should().Be("https://example.com/hook");
        result.IsActive.Should().BeTrue();
        result.LastDeliverySucceeded.Should().BeTrue();
    }
}
