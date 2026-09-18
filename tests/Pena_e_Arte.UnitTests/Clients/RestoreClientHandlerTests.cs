using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class RestoreClientHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private RestoreClientHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_RestoresClient_ClearsArchivedAt()
    {
        Client client = await SeedClient(archived: true);

        await CreateSut().Handle(new RestoreClientCommand(client.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AlreadyRestored_IsIdempotent_NotAnError()
    {
        Client client = await SeedClient(archived: false);

        await CreateSut().Handle(new RestoreClientCommand(client.Id), default);

        _db.Clients.Single(c => c.Id == client.Id).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownClientId_ThrowsNotFound()
    {
        Func<Task> act = () => CreateSut().Handle(new RestoreClientCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Client> SeedClient(bool archived)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
            ArchivedAt = archived ? DateTime.UtcNow : null,
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}
