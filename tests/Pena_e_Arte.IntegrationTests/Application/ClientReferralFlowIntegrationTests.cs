using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.ClientReferrals.Commands;
using Pena_e_Arte.Application.ClientReferrals.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class ClientReferralFlowIntegrationTests(DatabaseFixture fixture)
{
    private readonly ISlotLocker _locker = MakeAlwaysAvailableLocker();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    [Fact]
    public async Task GetOrCreateMyReferralCode_CalledTwice_IsIdempotent()
    {
        Guid studioId = await SeedStudio();
        Guid userId = Guid.NewGuid();
        await SeedClient(studioId, userId);

        ClientReferralCodeResponse first = await RunGetOrCreateCode(studioId, userId);
        ClientReferralCodeResponse second = await RunGetOrCreateCode(studioId, userId);

        second.Id.Should().Be(first.Id);
        second.Code.Should().Be(first.Code);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        (await verify.ClientReferralCodes.CountAsync(c => c.StudioId == studioId)).Should().Be(1);
    }

    [Fact]
    public async Task FullReferralFlow_RefereeBooksWithCode_ThenReferrerSpendsEarnedReward()
    {
        Guid studioId = await SeedStudio();
        await SeedFixedDeposit(studioId, 100m);

        Guid referrerUserId = Guid.NewGuid();
        Guid refereeUserId = Guid.NewGuid();
        Guid referrerClientId = await SeedClient(studioId, referrerUserId);
        Guid refereeClientId = await SeedClient(studioId, refereeUserId);
        Guid artistId = await SeedAvailableArtist(studioId);

        // 1. Referrer gets a code.
        ClientReferralCodeResponse code = await RunGetOrCreateCode(studioId, referrerUserId);

        // 2. Referee books using the code.
        CreateAppointmentRequest refereeReq = new(
            artistId, refereeClientId, DateTime.UtcNow.AddDays(3), 90, null, ReferralCode: code.Code);
        AppointmentResponse refereeBooking = await RunCreateAppointment(studioId, refereeReq);

        // 3. Referee's deposit was reduced.
        refereeBooking.DepositAmount.Should().Be(90m);

        // 4. Referrer now has a new unredeemed reward.
        IReadOnlyList<ClientReferralRewardResponse> rewards = await RunGetMyRewards(studioId, referrerUserId);
        rewards.Should().ContainSingle(r => !r.IsRedeemed);
        ClientReferralRewardResponse reward = rewards.Single();

        await using AppDbContext verifyRedemption = fixture.CreateDbContext(studioId);
        (await verifyRedemption.ClientReferralRedemptions.CountAsync(r =>
            r.RedeemedByClientId == refereeClientId)).Should().Be(1);

        // 5. Referrer books, spending their earned reward.
        CreateAppointmentRequest referrerReq = new(
            artistId, referrerClientId, DateTime.UtcNow.AddDays(4), 90, null, ReferralRewardId: reward.Id);
        AppointmentResponse referrerBooking = await RunCreateAppointment(studioId, referrerReq);

        // 6. Referrer's deposit was reduced, reward marked redeemed.
        referrerBooking.DepositAmount.Should().Be(90m);
        await using AppDbContext verifyReward = fixture.CreateDbContext(studioId);
        ClientReferralReward? updated = await verifyReward.ClientReferralRewards.FirstOrDefaultAsync(r => r.Id == reward.Id);
        updated!.IsRedeemed.Should().BeTrue();
        updated.RedeemedOnAppointmentId.Should().Be(referrerBooking.Id);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedStudio()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Client Referral Test Studio",
            Slug = ("cref-" + Guid.NewGuid().ToString("N"))[..20],
            City = "Porto",
            OwnerEmail = $"cref{Guid.NewGuid():N}@test.com",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        db.Studios.Add(studio);
        await db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task SeedFixedDeposit(Guid studioId, decimal amount)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        db.DepositRules.Add(new DepositRule { StudioId = studioId, Name = "Fixed", AmountFixed = amount, IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedClient(Guid studioId, Guid userId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Client client = new()
        {
            StudioId = studioId, UserId = userId, FirstName = "C", LastName = "D",
            Email = $"{Guid.NewGuid():N}@client.test",
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedAvailableArtist(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        Artist artist = new() { StudioId = studioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@artist.test" };
        db.Artists.Add(artist);
        await db.SaveChangesAsync();

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            db.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id, StudioId = studioId, DayOfWeek = day,
                StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
        }
        await db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<ClientReferralCodeResponse> RunGetOrCreateCode(Guid studioId, Guid userId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        GetOrCreateMyReferralCodeHandler handler = new(db, UserFor(userId));
        return await handler.Handle(new GetOrCreateMyReferralCodeCommand(), default);
    }

    private async Task<IReadOnlyList<ClientReferralRewardResponse>> RunGetMyRewards(Guid studioId, Guid userId)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        GetMyReferralRewardsHandler handler = new(db, UserFor(userId));
        return await handler.Handle(new GetMyReferralRewardsQuery(), default);
    }

    private async Task<AppointmentResponse> RunCreateAppointment(Guid studioId, CreateAppointmentRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(studioId);
        ICurrentUser artistUser = Substitute.For<ICurrentUser>();
        artistUser.Role.Returns("artist");
        CreateAppointmentHandler handler = new(
            db, TenantFor(studioId), artistUser, _locker, _jobs, _realtime, _sender, _planLimits);
        return await handler.Handle(new CreateAppointmentCommand(req), default);
    }

    private static ISlotLocker MakeAlwaysAvailableLocker()
    {
        ISlotLocker locker = Substitute.For<ISlotLocker>();
        locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
              .Returns(true);
        return locker;
    }

    private static ICurrentTenant TenantFor(Guid studioId)
    {
        CurrentTenantService t = new();
        t.SetTenant(studioId);
        return t;
    }

    private static ICurrentUser UserFor(Guid userId)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        return user;
    }
}
