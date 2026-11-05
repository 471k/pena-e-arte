using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// Phi, 2026-11-05 (E04b prompt §2.2): a no-show moves money, so an artist may only mark a session assigned
/// to them, and the preview follows the same rule. Owners and admins may mark any session in the studio.
/// Anything else is 404 and changes nothing, including no provider call.
/// </summary>
public class NoShowScopeTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly FakeCurrentUser _artist = FakeCurrentUser.Artist();
    private readonly Guid _myArtistId;
    private readonly Guid _colleagueArtistId;

    public NoShowScopeTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = _studioId.ToString("N")[..8], Currency = "EUR" });
        _myArtistId = SeedArtist(_artist.UserId);
        _colleagueArtistId = SeedArtist(Guid.NewGuid());
    }

    private MarkNoShowHandler Commit(ICurrentUser user) =>
        new(_db, _tenant, Substitute.For<IRealtimeNotifier>(), _provider, user);

    private GetNoShowPreviewHandler Preview(ICurrentUser user) => new(_db, user);

    [Fact]
    public async Task AnArtist_CanMarkTheirOwnSession_AndPreviewIt()
    {
        Guid id = SeedHeldDeposit(_myArtistId);

        NoShowPreviewResponse preview = await Preview(_artist).Handle(new GetNoShowPreviewQuery(id), default);
        NoShowResultResponse result = await Commit(_artist).Handle(new MarkNoShowCommand(id), default);

        preview.Outcome.Should().Be("HeldDepositCaptured");
        result.Outcome.Should().Be("HeldDepositCaptured");
        await _provider.Received(1).CaptureAsync(_studioId, "pok_held", Arg.Any<CancellationToken>());
    }

    public static TheoryData<string> OutOfScope => new() { "colleague", "unassigned", "no profile" };

    [Theory]
    [MemberData(nameof(OutOfScope))]
    public async Task AnArtist_GetsNotFound_AndNothingChanges_ForASessionThatIsNotTheirs(string which)
    {
        Guid? artistId = which == "colleague" ? _colleagueArtistId : which == "unassigned" ? null : _myArtistId;
        ICurrentUser caller = which == "no profile" ? FakeCurrentUser.Artist() : _artist;
        Guid id = SeedHeldDeposit(artistId);

        Func<Task> preview = () => Preview(caller).Handle(new GetNoShowPreviewQuery(id), default);
        Func<Task> commit = () => Commit(caller).Handle(new MarkNoShowCommand(id), default);

        await preview.Should().ThrowAsync<NotFoundException>();
        await commit.Should().ThrowAsync<NotFoundException>();
        await _provider.DidNotReceiveWithAnyArgs().CaptureAsync(default, default!, default);
        Appointment after = _db.Appointments.Single(a => a.Id == id);
        after.Status.Should().Be(AppointmentStatus.Confirmed);
        after.DepositStatus.Should().Be(DepositStatus.Pending);
        _db.Payments.Single(p => p.AppointmentId == id).Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task AnOwner_CanMarkAnyArtistsSession_AndAnUnassignedOne()
    {
        Guid colleagues = SeedHeldDeposit(_colleagueArtistId);
        Guid unassigned = SeedHeldDeposit(null);

        (await Commit(FakeCurrentUser.Owner()).Handle(new MarkNoShowCommand(colleagues), default)).Appointment.Status.Should().Be("NoShow");
        (await Preview(FakeCurrentUser.Owner()).Handle(new GetNoShowPreviewQuery(unassigned), default)).CanMarkNoShow.Should().BeTrue();
    }

    [Fact]
    public async Task AnAdmin_IsNotLimitedEither()
    {
        Guid id = SeedHeldDeposit(_colleagueArtistId);

        (await Commit(FakeCurrentUser.Admin()).Handle(new MarkNoShowCommand(id), default)).Appointment.Status.Should().Be("NoShow");
    }

    private Guid SeedArtist(Guid userId)
    {
        Artist artist = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist.Id;
    }

    private Guid SeedHeldDeposit(Guid? artistId)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddHours(-2),
            EndDate = DateTime.UtcNow.AddHours(-1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 80m,
        };
        _db.Appointments.Add(appointment);
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appointment.Id,
            ClientId = appointment.ClientId,
            Amount = 80m,
            Currency = "EUR",
            Status = PaymentStatus.Captured,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pok_held",
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }
}
