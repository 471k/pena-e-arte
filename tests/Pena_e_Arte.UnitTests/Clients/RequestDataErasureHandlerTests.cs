using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class RequestDataErasureHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private RequestDataErasureHandler CreateSut() => new(_db, _identity);

    [Fact]
    public async Task Handle_ErasesOnlyTheTargetedClient_NeverAnotherClients()
    {
        Guid targetUserId = Guid.NewGuid();
        (Guid targetClientId, Guid targetFormId) = await SeedClientWithData(targetUserId);
        (Guid otherClientId, Guid otherFormId) = await SeedClientWithData(Guid.NewGuid());

        await CreateSut().Handle(new RequestDataErasureCommand(targetClientId), default);

        // Targeted client's data soft-deleted, marked for anonymization, login disabled.
        _db.ConsentForms.Single(f => f.Id == targetFormId).DeletedAt.Should().NotBeNull();
        _db.ClientProfiles.Single(p => p.ClientId == targetClientId).DeletedAt.Should().NotBeNull();
        _db.Clients.Single(c => c.Id == targetClientId).ErasureRequestedAt.Should().NotBeNull();
        await _identity.Received(1).DisableLoginAsync(targetUserId, Arg.Any<CancellationToken>());

        // The OTHER client's data is completely untouched.
        _db.ConsentForms.Single(f => f.Id == otherFormId).DeletedAt.Should().BeNull();
        _db.ClientProfiles.Single(p => p.ClientId == otherClientId).DeletedAt.Should().BeNull();
        _db.Clients.Single(c => c.Id == otherClientId).ErasureRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ClientWithNoLogin_SkipsDisableLogin()
    {
        (Guid clientId, _) = await SeedClientWithData(userId: null);

        await CreateSut().Handle(new RequestDataErasureCommand(clientId), default);

        _db.Clients.Single(c => c.Id == clientId).ErasureRequestedAt.Should().NotBeNull();
        await _identity.DidNotReceive().DisableLoginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownClientId_ThrowsNotFound()
    {
        Func<Task> act = () => CreateSut().Handle(new RequestDataErasureCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Command_ExposesAuditFields_ForTheTargetedClient()
    {
        Guid clientId = Guid.NewGuid();
        RequestDataErasureCommand command = new(clientId);

        command.AuditTargetId.Should().Be(clientId);
        command.AuditAction.Should().NotBeNullOrWhiteSpace();
    }

    private async Task<(Guid ClientId, Guid FormId)> SeedClientWithData(Guid? userId)
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        ConsentForm form = new()
        {
            StudioId = _studioId,
            ClientId = client.Id,
            AppointmentId = Guid.NewGuid(),
            SignedAt = DateTime.UtcNow,
        };
        _db.ConsentForms.Add(form);
        _db.ClientProfiles.Add(new ClientProfile { StudioId = _studioId, ClientId = client.Id });
        await _db.SaveChangesAsync();

        return (client.Id, form.Id);
    }
}
