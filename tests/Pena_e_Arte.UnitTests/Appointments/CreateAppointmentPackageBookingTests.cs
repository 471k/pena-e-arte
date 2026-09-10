using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CreateAppointmentPackageBookingTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ISlotLocker _locker = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    public CreateAppointmentPackageBookingTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.Role.Returns("artist"); // ClientId is trusted from the request on this path
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);

        _db.Clients.Add(new Client { Id = _clientId, StudioId = _studioId, FirstName = "C", LastName = "L", Email = "c@l.com" });
        _db.SaveChanges();
    }

    private CreateAppointmentHandler CreateSut() =>
        new(_db, _tenant, _user, _locker, _jobs, _realtime, _sender, _planLimits);

    [Fact]
    public async Task Handle_PackageCoveredBooking_DecrementsSessionsRemainingByOne()
    {
        Guid artistId = SeedArtist();
        PackagePurchase purchase = SeedPurchase(sessionsRemaining: 5);

        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest(artistId, purchase.Id)), default);

        _db.PackagePurchases.Single(p => p.Id == purchase.Id).SessionsRemaining.Should().Be(4);
    }

    [Fact]
    public async Task Handle_PackageCoveredBooking_SetsDepositAmountToZeroAndPrePaid()
    {
        Guid artistId = SeedArtist();
        PackagePurchase purchase = SeedPurchase(sessionsRemaining: 5);

        AppointmentResponse result = await CreateSut().Handle(
            new CreateAppointmentCommand(ValidRequest(artistId, purchase.Id)), default);

        result.DepositAmount.Should().Be(0m);
        result.DepositStatus.Should().Be(DepositStatus.PrePaid.ToString());
    }

    [Fact]
    public async Task Handle_PackageWithNoSessionsRemaining_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtist();
        PackagePurchase purchase = SeedPurchase(sessionsRemaining: 0);

        Func<Task> act = () => CreateSut().Handle(
            new CreateAppointmentCommand(ValidRequest(artistId, purchase.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PackageBelongingToDifferentClient_ThrowsNotFoundException()
    {
        Guid artistId = SeedArtist();
        PackagePurchase purchase = SeedPurchase(sessionsRemaining: 3, clientId: Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(
            new CreateAppointmentCommand(ValidRequest(artistId, purchase.Id)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoPackagePurchaseId_UsesNormalDepositCalculation()
    {
        Guid artistId = SeedArtist();

        AppointmentResponse result = await CreateSut().Handle(
            new CreateAppointmentCommand(ValidRequest(artistId, null)), default);

        result.DepositStatus.Should().Be(DepositStatus.Pending.ToString());
    }

    private Guid SeedArtist()
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "Art", LastName = "Ist", Email = "art@ist.com" };
        _db.Artists.Add(artist);

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            _db.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            _db.StudioHours.Add(new StudioHours
            {
                StudioId = _studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsOpen = true,
            });
        }
        _db.SaveChanges();
        return artist.Id;
    }

    private PackagePurchase SeedPurchase(int sessionsRemaining, Guid? clientId = null)
    {
        Package package = new() { StudioId = _studioId, Name = "5-Pack", SessionCount = 5, Price = 500m, IsActive = true };
        _db.Packages.Add(package);

        PackagePurchase purchase = new()
        {
            StudioId = _studioId,
            PackageId = package.Id,
            ClientId = clientId ?? _clientId,
            SessionsRemaining = sessionsRemaining,
            ProviderReferenceId = "pi_test",
            Provider = "pok",
            ConfirmedAt = DateTime.UtcNow,
        };
        _db.PackagePurchases.Add(purchase);
        _db.SaveChanges();
        return purchase;
    }

    private CreateAppointmentRequest ValidRequest(Guid artistId, Guid? packagePurchaseId) =>
        new(artistId, _clientId, DateTime.UtcNow.AddDays(3), 90, null,
            PackagePurchaseId: packagePurchaseId);
}
