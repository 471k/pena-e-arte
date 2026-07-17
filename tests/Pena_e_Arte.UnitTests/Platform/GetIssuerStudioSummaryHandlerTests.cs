using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetIssuerStudioSummaryHandlerTests
{
    private readonly FakeDbContext    _db       = FakeDbContext.Create();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private GetIssuerStudioSummaryHandler CreateSut() => new(_db, _identity);

    [Fact]
    public async Task Handle_StudioWithData_ReturnsCorrectCounts()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Soul", Slug = "ink-soul", OwnerEmail = "owner@ink-soul.test" });
        _db.Artists.Add(new Artist { StudioId = studioId, FirstName = "A", LastName = "1", Email = "a1@test.com" });
        _db.Artists.Add(new Artist { StudioId = studioId, FirstName = "A", LastName = "2", Email = "a2@test.com" });
        _db.Clients.Add(new Client { StudioId = studioId, FirstName = "C", LastName = "1", Email = "c1@test.com" });
        _db.Appointments.Add(new Appointment { StudioId = studioId, ArtistId = Guid.NewGuid(), ClientId = Guid.NewGuid(), Date = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddHours(1) });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _identity.GetUserDisplayNameAsync("owner@ink-soul.test", Arg.Any<CancellationToken>())
            .Returns((string?)"Maria Silva");

        IssuerStudioSummaryResponse result =
            await CreateSut().Handle(new GetIssuerStudioSummaryQuery(studioId), default);

        result.ArtistCount.Should().Be(2);
        result.ClientCount.Should().Be(1);
        result.AppointmentCount.Should().Be(1);
        result.OwnerEmail.Should().Be("owner@ink-soul.test");
        result.OwnerDisplayName.Should().Be("Maria Silva");
    }

    [Fact]
    public async Task Handle_NoOwnerEmail_ReturnsPlaceholderForOwnerFields()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "No Owner Studio", Slug = "no-owner-studio", OwnerEmail = "" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        IssuerStudioSummaryResponse result =
            await CreateSut().Handle(new GetIssuerStudioSummaryQuery(studioId), default);

        result.OwnerEmail.Should().Be("—");
        result.OwnerDisplayName.Should().Be("—");
    }

    [Fact]
    public async Task Handle_StudioWithNoAssociatedData_ReturnsZeroCounts()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Empty Studio", Slug = "empty-studio", OwnerEmail = "owner@empty.test" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _identity.GetUserDisplayNameAsync("owner@empty.test", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        IssuerStudioSummaryResponse result =
            await CreateSut().Handle(new GetIssuerStudioSummaryQuery(studioId), default);

        result.ArtistCount.Should().Be(0);
        result.ClientCount.Should().Be(0);
        result.AppointmentCount.Should().Be(0);
        result.OwnerDisplayName.Should().Be("owner@empty.test");
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetIssuerStudioSummaryQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
