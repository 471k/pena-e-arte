using FluentAssertions;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetClientsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetClientsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoSearch_ReturnsAllClientsOrderedByLastNameThenFirstName()
    {
        await SeedClients(
            ("Carlos", "Silva", "carlos@example.com"),
            ("Ana", "Pereira", "ana@example.com"),
            ("Beatriz", "Silva", "beatriz@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery(null), default);

        result.Should().HaveCount(3);
        result[0].LastName.Should().Be("Pereira");
        result[1].FirstName.Should().Be("Beatriz");
        result[2].FirstName.Should().Be("Carlos");
    }

    [Fact]
    public async Task Handle_SearchMatchesFirstName_ReturnsMatchingClients()
    {
        await SeedClients(
            ("Rui", "Neves", "rui@example.com"),
            ("Maria", "Neves", "maria@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("Rui"), default);

        result.Should().ContainSingle(c => c.FirstName == "Rui");
    }

    [Fact]
    public async Task Handle_SearchMatchesLastName_ReturnsMatchingClients()
    {
        await SeedClients(
            ("Ana", "Ferreira", "ana@example.com"),
            ("Rui", "Neves", "rui@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("Ferreira"), default);

        result.Should().ContainSingle(c => c.LastName == "Ferreira");
    }

    [Fact]
    public async Task Handle_SearchMatchesEmail_ReturnsMatchingClients()
    {
        await SeedClients(
            ("Ana", "Costa", "ana@studio.com"),
            ("Rui", "Gomes", "rui@other.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("studio.com"), default);

        result.Should().ContainSingle(c => c.Email == "ana@studio.com");
    }

    [Fact]
    public async Task Handle_SearchIsCaseInsensitive_ReturnsMatches()
    {
        await SeedClients(("Fernanda", "Lima", "fernanda@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("FERNANDA"), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhitespaceSearch_ReturnsAllClients()
    {
        await SeedClients(
            ("A", "B", "a@example.com"),
            ("C", "D", "c@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("   "), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoMatches_ReturnsEmptyList()
    {
        await SeedClients(("Ana", "Costa", "ana@example.com"));

        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery("zzznomatch"), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmptyList()
    {
        List<ClientResponse> result = await CreateSut().Handle(new GetClientsQuery(null), default);

        result.Should().BeEmpty();
    }

    private async Task SeedClients(params (string First, string Last, string Email)[] clients)
    {
        foreach ((string first, string last, string email) in clients)
            _db.Clients.Add(new Client { StudioId = _studioId, FirstName = first, LastName = last, Email = email });

        await _db.SaveChangesAsync();
    }
}
