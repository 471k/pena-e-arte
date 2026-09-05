using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class CreateOwnArtistProfileHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly FakeCurrentUser _owner;

    public CreateOwnArtistProfileHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _owner = new FakeCurrentUser(Guid.NewGuid(), "owner", "owner@studio.com");
    }

    private CreateOwnArtistProfileHandler CreateSut() => new(_db, _tenant, _owner, _planLimits);

    [Fact]
    public void CreateOwnArtistProfileCommand_IsQuotaCheckedForArtists()
    {
        IQuotaCheckedCommand command =
            new CreateOwnArtistProfileCommand(new CreateOwnArtistProfileRequest("A", "B", null));

        command.QuotaType.Should().Be(QuotaType.Artists);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_CreatesArtistLinkedToCallersOwnUserId()
    {
        CreateOwnArtistProfileRequest req = new("Rui", "Tavares", "Neo-traditional", 90m);

        ArtistResponse result = await CreateSut().Handle(new CreateOwnArtistProfileCommand(req), default);

        result.UserId.Should().Be(_owner.UserId);
        result.Email.Should().Be("owner@studio.com");
        result.StudioId.Should().Be(_studioId);
        result.FirstName.Should().Be("Rui");
        result.Specializations.Should().Be("Neo-traditional");
        result.HourlyRate.Should().Be(90m);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_PersistsArtistToDb()
    {
        await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        _db.Artists.Should().ContainSingle(a => a.UserId == _owner.UserId && a.Email == "owner@studio.com");
    }

    [Fact]
    public async Task Handle_AlreadyHasProfile_ThrowsBusinessRuleViolationException()
    {
        _db.Artists.Add(new Artist
        {
            StudioId = _studioId,
            UserId = _owner.UserId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "owner@studio.com",
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AlreadyHasProfile_DoesNotPersistSecondArtist()
    {
        _db.Artists.Add(new Artist
        {
            StudioId = _studioId,
            UserId = _owner.UserId,
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "owner@studio.com",
        });
        await _db.SaveChangesAsync();

        try
        {
            await CreateSut().Handle(new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);
        }
        catch { }

        _db.Artists.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_NoExistingProfile_InvalidatesArtistsUsageCache()
    {
        await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        await _planLimits.Received(1)
            .InvalidateUsageCacheAsync(QuotaType.Artists, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateSlugSource_AppendsSuffixForUniqueness()
    {
        Artist existing = new()
        {
            StudioId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FirstName = "Rui",
            LastName = "Tavares",
            Email = "someone-else@other-studio.com",
        };
        existing.SetSlug("rui-tavares");
        _db.Artists.Add(existing);
        await _db.SaveChangesAsync();

        ArtistResponse result = await CreateSut().Handle(
            new CreateOwnArtistProfileCommand(new("Rui", "Tavares", null)), default);

        result.Slug.Should().Be("rui-tavares-2");
    }
}
