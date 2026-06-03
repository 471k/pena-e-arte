using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class CreateArtistHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public CreateArtistHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateArtistHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NewEmail_ReturnsArtistResponse()
    {
        CreateArtistRequest req = new("Rui", "Tavares", "rui@studio.com", "Neo-traditional, Realism");

        ArtistResponse result = await CreateSut().Handle(new CreateArtistCommand(req), default);

        result.FirstName.Should().Be("Rui");
        result.LastName.Should().Be("Tavares");
        result.Email.Should().Be("rui@studio.com");
        result.Specializations.Should().Be("Neo-traditional, Realism");
        result.StudioId.Should().Be(_studioId);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_NewEmail_PersistsArtistToDb()
    {
        CreateArtistRequest req = new("Rui", "Tavares", "rui@studio.com", null);

        await CreateSut().Handle(new CreateArtistCommand(req), default);

        _db.Artists.Should().ContainSingle(a => a.Email == "rui@studio.com");
    }

    [Fact]
    public async Task Handle_NullSpecializations_ReturnsArtistWithNullSpecializations()
    {
        CreateArtistRequest req = new("Ana", "Lima", "ana@studio.com", null);

        ArtistResponse result = await CreateSut().Handle(new CreateArtistCommand(req), default);

        result.Specializations.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsBusinessRuleViolationException()
    {
        const string email = "duplicate@studio.com";
        _db.Artists.Add(new Artist
        {
            StudioId  = _studioId,
            FirstName = "Existing",
            LastName  = "Artist",
            Email     = email
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateArtistCommand(new("New", "Artist", email, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage($"*{email}*");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_DoesNotPersistSecondArtist()
    {
        const string email = "duplicate@studio.com";
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = email });
        await _db.SaveChangesAsync();

        try { await CreateSut().Handle(new CreateArtistCommand(new("C", "D", email, null)), default); } catch { }

        _db.Artists.Should().ContainSingle(a => a.Email == email);
    }
}
