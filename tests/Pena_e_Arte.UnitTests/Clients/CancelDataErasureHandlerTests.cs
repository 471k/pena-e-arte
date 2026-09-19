using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class CancelDataErasureHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private CancelDataErasureHandler CreateSut() => new(_db, _identity);

    [Fact]
    public async Task Handle_TwoStudioErasure_CancelledFromOneStudio_RestoresBoth()
    {
        Guid userId = Guid.NewGuid();
        DateTime requestedAt = DateTime.UtcNow.AddDays(-5);

        (Client clientA, ConsentForm formA, ClientProfile profileA) =
            await SeedErasedClient(userId, requestedAt);
        (Client clientB, ConsentForm formB, ClientProfile profileB) =
            await SeedErasedClient(userId, requestedAt);

        await CreateSut().Handle(new CancelDataErasureCommand(clientA.Id), default);

        _db.Clients.Single(c => c.Id == clientA.Id).ErasureRequestedAt.Should().BeNull();
        _db.Clients.Single(c => c.Id == clientB.Id).ErasureRequestedAt.Should().BeNull();
        _db.ConsentForms.Single(f => f.Id == formA.Id).DeletedAt.Should().BeNull();
        _db.ConsentForms.Single(f => f.Id == formB.Id).DeletedAt.Should().BeNull();
        _db.ClientProfiles.Single(p => p.ClientId == clientA.Id).DeletedAt.Should().BeNull();
        _db.ClientProfiles.Single(p => p.ClientId == clientB.Id).DeletedAt.Should().BeNull();
        await _identity.Received(1).EnableLoginAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FormIndependentlyExpiredByRetention_IsNotResurrected()
    {
        Guid userId = Guid.NewGuid();
        DateTime requestedAt = DateTime.UtcNow.AddDays(-5);
        (Client client, _, _) = await SeedErasedClient(userId, requestedAt);

        // A second form, soft-deleted by the routine retention pass at a DIFFERENT timestamp —
        // unrelated to this erasure request.
        ConsentForm unrelatedForm = new()
        {
            StudioId = client.StudioId,
            ClientId = client.Id,
            AppointmentId = Guid.NewGuid(),
            SignedAt = DateTime.UtcNow.AddYears(-8),
            DeletedAt = DateTime.UtcNow.AddDays(-100),
        };
        _db.ConsentForms.Add(unrelatedForm);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new CancelDataErasureCommand(client.Id), default);

        _db.ConsentForms.Single(f => f.Id == unrelatedForm.Id).DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoPendingErasureRequest_ThrowsBusinessRuleViolation()
    {
        Client client = new()
        {
            StudioId = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new CancelDataErasureCommand(client.Id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_UnknownClientId_ThrowsNotFound()
    {
        Func<Task> act = () => CreateSut().Handle(new CancelDataErasureCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<(Client Client, ConsentForm Form, ClientProfile Profile)> SeedErasedClient(
        Guid userId, DateTime requestedAt)
    {
        Guid studioId = Guid.NewGuid();
        Client client = new()
        {
            StudioId = studioId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
            ErasureRequestedAt = requestedAt,
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        ConsentForm form = new()
        {
            StudioId = studioId,
            ClientId = client.Id,
            AppointmentId = Guid.NewGuid(),
            SignedAt = DateTime.UtcNow,
            DeletedAt = requestedAt,
        };
        _db.ConsentForms.Add(form);

        ClientProfile profile = new()
        {
            StudioId = studioId,
            ClientId = client.Id,
            DeletedAt = requestedAt,
        };
        _db.ClientProfiles.Add(profile);
        await _db.SaveChangesAsync();

        return (client, form, profile);
    }
}
