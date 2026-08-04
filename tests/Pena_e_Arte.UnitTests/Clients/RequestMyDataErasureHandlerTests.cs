using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class RequestMyDataErasureHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private RequestMyDataErasureHandler CreateSut() => new(_db, _currentUser, _identity);

    [Fact]
    public async Task Handle_ErasesOnlyTheCallersOwnData_NeverAnotherClients()
    {
        Guid myUserId = Guid.NewGuid();
        (Guid myClientId, Guid myFormId) = await SeedClientWithData(myUserId);
        (Guid otherClientId, Guid otherFormId) = await SeedClientWithData(Guid.NewGuid());

        _currentUser.UserId.Returns(myUserId);

        await CreateSut().Handle(new RequestMyDataErasureCommand(), default);

        // My data soft-deleted and the account marked for anonymization + login disabled.
        _db.ConsentForms.Single(f => f.Id == myFormId).DeletedAt.Should().NotBeNull();
        _db.ClientProfiles.Single(p => p.ClientId == myClientId).DeletedAt.Should().NotBeNull();
        _db.Clients.Single(c => c.Id == myClientId).ErasureRequestedAt.Should().NotBeNull();
        await _identity.Received(1).DisableLoginAsync(myUserId, Arg.Any<CancellationToken>());

        // The OTHER client's data is completely untouched (IDOR-proof: no id from the request).
        _db.ConsentForms.Single(f => f.Id == otherFormId).DeletedAt.Should().BeNull();
        _db.ClientProfiles.Single(p => p.ClientId == otherClientId).DeletedAt.Should().BeNull();
        _db.Clients.Single(c => c.Id == otherClientId).ErasureRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SetsResolvedClientId_FromCurrentUser_ForAudit()
    {
        Guid myUserId = Guid.NewGuid();
        (Guid myClientId, _) = await SeedClientWithData(myUserId);
        _currentUser.UserId.Returns(myUserId);

        RequestMyDataErasureCommand command = new();
        await CreateSut().Handle(command, default);

        command.ResolvedClientId.Should().Be(myClientId);
        command.AuditTargetId.Should().Be(myClientId);
    }

    [Fact]
    public async Task Handle_NoClientForCaller_ThrowsNotFound()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new RequestMyDataErasureCommand(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<(Guid ClientId, Guid FormId)> SeedClientWithData(Guid userId)
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
