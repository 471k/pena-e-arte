using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class ArchiveClientHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private ArchiveClientHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ArchivesClient_SetsArchivedAt()
    {
        Client client = await SeedClient();

        await CreateSut().Handle(new ArchiveClientCommand(client.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Archiving_DoesNotTouchDeletedAtErasureOrRelatedData()
    {
        Client client = await SeedClient();
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

        await CreateSut().Handle(new ArchiveClientCommand(client.Id), default);

        Client updated = _db.Clients.Single(c => c.Id == client.Id);
        updated.DeletedAt.Should().BeNull();
        updated.ErasureRequestedAt.Should().BeNull();
        _db.ConsentForms.Single(f => f.Id == form.Id).DeletedAt.Should().BeNull();
        _db.ClientProfiles.Single(p => p.ClientId == client.Id).DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlreadyArchived_IsIdempotent_NotAnError()
    {
        Client client = await SeedClient();
        client.ArchivedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        DateTime originalArchivedAt = client.ArchivedAt.Value;

        await CreateSut().Handle(new ArchiveClientCommand(client.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArchivedAt.Should().Be(originalArchivedAt);
    }

    [Fact]
    public async Task Handle_UnknownClientId_ThrowsNotFound()
    {
        Func<Task> act = () => CreateSut().Handle(new ArchiveClientCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Client> SeedClient()
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}
