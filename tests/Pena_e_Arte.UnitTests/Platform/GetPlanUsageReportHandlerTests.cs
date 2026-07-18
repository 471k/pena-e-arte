using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetPlanUsageReportHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPlanUsageReportHandler CreateSut() => new(_db);

    private Studio SeedStudioWithPlan(Plan plan)
    {
        Studio studio = new()
        {
            Name       = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug       = Guid.NewGuid().ToString("N")[..20],
            City       = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive   = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        _db.Plans.Add(plan);
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            PlanId           = plan.Id,
            Status           = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd   = DateTime.UtcNow.AddDays(37),
        });
        return studio;
    }

    [Fact]
    public async Task Handle_NoStudios_ReturnsEmptyList()
    {
        PlanUsageReportResponse result = await CreateSut().Handle(new GetPlanUsageReportQuery(), default);

        result.Studios.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StudioWithNoSubscription_IsExcluded()
    {
        _db.Studios.Add(new Studio
        {
            Name = "No Plan", Slug = "no-plan", City = "Porto",
            OwnerEmail = "owner@test.com", IsActive = true,
        });
        await _db.SaveChangesAsync();

        PlanUsageReportResponse result = await CreateSut().Handle(new GetPlanUsageReportQuery(), default);

        result.Studios.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SingleStudioWithPartialUsage_ReturnsCorrectCounts()
    {
        Studio studio = SeedStudioWithPlan(new Plan
        {
            Name = "Starter", MaxArtists = 6, MaxAppointmentsPerMonth = 40,
            MaxNotificationsPerMonth = 150, MaxStorageGb = 2,
        });
        await _db.SaveChangesAsync();

        _db.Artists.Add(new Artist { StudioId = studio.Id, FirstName = "A", LastName = "B", Email = "a@x.com" });
        _db.Artists.Add(new Artist { StudioId = studio.Id, FirstName = "C", LastName = "D", Email = "c@x.com" });
        _db.Appointments.Add(new Appointment
        {
            StudioId = studio.Id, ArtistId = Guid.NewGuid(), ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddHours(1), DurationMinutes = 60,
            Status = AppointmentStatus.Pending, DepositStatus = DepositStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        studio.StorageUsageBytes = (long)(1.5 * 1024 * 1024 * 1024);
        await _db.SaveChangesAsync();

        PlanUsageReportResponse result = await CreateSut().Handle(new GetPlanUsageReportQuery(), default);

        result.Studios.Should().HaveCount(1);
        StudioPlanUsageRow row = result.Studios[0];
        row.PlanName.Should().Be("Starter");
        row.ArtistCount.Should().Be(2);
        row.MaxArtists.Should().Be(6);
        row.AppointmentsThisMonth.Should().Be(1);
        row.MaxAppointmentsPerMonth.Should().Be(40);
        row.NotificationsThisMonth.Should().Be(0);
        row.MaxNotificationsPerMonth.Should().Be(150);
        row.StorageGbUsed.Should().Be(1.5);
        row.MaxStorageGb.Should().Be(2);
    }

    [Fact]
    public async Task Handle_UnlimitedPlan_MaxFieldsAreNull()
    {
        SeedStudioWithPlan(new Plan { Name = "Pro" });
        await _db.SaveChangesAsync();

        PlanUsageReportResponse result = await CreateSut().Handle(new GetPlanUsageReportQuery(), default);

        result.Studios.Should().HaveCount(1);
        StudioPlanUsageRow row = result.Studios[0];
        row.MaxArtists.Should().BeNull();
        row.MaxAppointmentsPerMonth.Should().BeNull();
        row.MaxNotificationsPerMonth.Should().BeNull();
        row.MaxStorageGb.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MultipleStudios_SortsClosestToCapFirst()
    {
        // Studio A: 1 of 6 artists (17%) — far from cap
        Studio studioA = SeedStudioWithPlan(new Plan { Name = "Pro", MaxArtists = 6 });
        // Studio B: 5 of 6 artists (83%) — close to cap
        Studio studioB = SeedStudioWithPlan(new Plan { Name = "Pro", MaxArtists = 6 });
        await _db.SaveChangesAsync();

        _db.Artists.Add(new Artist { StudioId = studioA.Id, FirstName = "A", LastName = "1", Email = "a1@x.com" });

        for (int i = 0; i < 5; i++)
            _db.Artists.Add(new Artist { StudioId = studioB.Id, FirstName = "B", LastName = $"{i}", Email = $"b{i}@x.com" });

        await _db.SaveChangesAsync();

        PlanUsageReportResponse result = await CreateSut().Handle(new GetPlanUsageReportQuery(), default);

        result.Studios.Should().HaveCount(2);
        result.Studios[0].StudioId.Should().Be(studioB.Id); // closest to cap first
        result.Studios[1].StudioId.Should().Be(studioA.Id);
    }
}
