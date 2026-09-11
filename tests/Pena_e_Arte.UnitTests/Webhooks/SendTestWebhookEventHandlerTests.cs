using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Webhooks.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Webhooks;

public class SendTestWebhookEventHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    public SendTestWebhookEventHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private SendTestWebhookEventHandler CreateSut() => new(_db, _tenant, _jobs);

    [Fact]
    public async Task Handle_NoActiveEndpoint_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(new SendTestWebhookEventCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_InactiveEndpointOnly_ThrowsBusinessRuleViolationException()
    {
        _db.WebhookEndpoints.Add(new WebhookEndpoint { StudioId = _studioId, Url = "https://x.com", IsActive = false });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new SendTestWebhookEventCommand(), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ActiveEndpoint_EnqueuesPingDelivery()
    {
        _db.WebhookEndpoints.Add(new WebhookEndpoint { StudioId = _studioId, Url = "https://x.com", IsActive = true });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new SendTestWebhookEventCommand(), default);

        _jobs.Received(1).EnqueueWebhookDelivery(_studioId, "ping", Guid.Empty);
    }
}
