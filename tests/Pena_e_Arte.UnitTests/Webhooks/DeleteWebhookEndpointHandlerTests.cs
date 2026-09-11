using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Webhooks.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Webhooks;

public class DeleteWebhookEndpointHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public DeleteWebhookEndpointHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private DeleteWebhookEndpointHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoEndpoint_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeleteWebhookEndpointCommand(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ExistingEndpoint_SoftDeletesIt()
    {
        WebhookEndpoint endpoint = new() { StudioId = _studioId, Url = "https://example.com/hook" };
        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteWebhookEndpointCommand(), default);

        endpoint.DeletedAt.Should().NotBeNull();
    }
}
