using FluentAssertions;
using Pena_e_Arte.Application.ExternalApi.Queries;
using Pena_e_Arte.Contracts.Responses.ExternalApi;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExternalApi;

public class GetExternalClientsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetExternalClientsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ReturnsClientsOrderedByLastThenFirstName()
    {
        _db.Clients.Add(new Client { StudioId = _studioId, FirstName = "Ana", LastName = "Silva", Email = "ana@test.com" });
        _db.Clients.Add(new Client { StudioId = _studioId, FirstName = "Rui", LastName = "Costa", Email = "rui@test.com" });
        await _db.SaveChangesAsync();

        List<ExternalClientResponse> result = await CreateSut().Handle(new GetExternalClientsQuery(), default);

        result.Should().HaveCount(2);
        result[0].LastName.Should().Be("Costa");
        result[1].LastName.Should().Be("Silva");
    }

    [Fact]
    public async Task Handle_DoesNotExposeErasureOrInternalFields()
    {
        _db.Clients.Add(new Client
        {
            StudioId = _studioId, FirstName = "Ana", LastName = "Silva",
            Email = "ana@test.com", Phone = "+351900000000",
        });
        await _db.SaveChangesAsync();

        ExternalClientResponse result = (await CreateSut().Handle(new GetExternalClientsQuery(), default)).Single();

        result.FirstName.Should().Be("Ana");
        result.Phone.Should().Be("+351900000000");
    }
}
