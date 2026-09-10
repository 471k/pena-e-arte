using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.BoothRent.Commands;
using Pena_e_Arte.Application.BoothRent.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class BoothRentHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetBoothRentCharges_ArtistRole_SeesOnlyOwnCharges()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userIdA = Guid.NewGuid();
        Guid artistAId = await SeedArtist(tenantId, userIdA);
        Guid artistBId = await SeedArtist(tenantId, Guid.NewGuid());

        await SeedCharge(tenantId, artistAId);
        await SeedCharge(tenantId, artistBId);

        ICurrentUser artistUser = Substitute.For<ICurrentUser>();
        artistUser.Role.Returns("artist");
        artistUser.UserId.Returns(userIdA);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetBoothRentChargesHandler handler = new(db, artistUser);
        List<BoothRentChargeResponse> result = await handler.Handle(new GetBoothRentChargesQuery(), default);

        result.Should().OnlyContain(c => c.ArtistId == artistAId);
    }

    [Fact]
    public async Task GetBoothRentCharges_OwnerRole_SeesAllCharges()
    {
        Guid tenantId = Guid.NewGuid();
        Guid artistAId = await SeedArtist(tenantId, Guid.NewGuid());
        Guid artistBId = await SeedArtist(tenantId, Guid.NewGuid());

        await SeedCharge(tenantId, artistAId);
        await SeedCharge(tenantId, artistBId);

        ICurrentUser ownerUser = Substitute.For<ICurrentUser>();
        ownerUser.Role.Returns("owner");

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetBoothRentChargesHandler handler = new(db, ownerUser);
        List<BoothRentChargeResponse> result = await handler.Handle(new GetBoothRentChargesQuery(), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task MarkBoothRentChargeSettled_SetsIsSettledAndTimestamp()
    {
        Guid tenantId = Guid.NewGuid();
        Guid artistId = await SeedArtist(tenantId, Guid.NewGuid());
        Guid chargeId = await SeedCharge(tenantId, artistId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        MarkBoothRentChargeSettledHandler handler = new(db);

        BoothRentChargeResponse result = await handler.Handle(
            new MarkBoothRentChargeSettledCommand(chargeId, new MarkBoothRentChargeSettledRequest("Paid in cash")), default);

        result.IsSettled.Should().BeTrue();
        result.SettledNote.Should().Be("Paid in cash");

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        BoothRentCharge charge = await verify.BoothRentCharges.FirstAsync(c => c.Id == chargeId);
        charge.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBoothRentCharges_TenantIsolation_DoesNotLeakOtherStudiosCharges()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid artistA = await SeedArtist(tenantA, Guid.NewGuid());
        Guid artistB = await SeedArtist(tenantB, Guid.NewGuid());

        await SeedCharge(tenantA, artistA);
        await SeedCharge(tenantB, artistB);

        ICurrentUser ownerUser = Substitute.For<ICurrentUser>();
        ownerUser.Role.Returns("owner");

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetBoothRentChargesHandler handler = new(db, ownerUser);
        List<BoothRentChargeResponse> result = await handler.Handle(new GetBoothRentChargesQuery(), default);

        result.Should().OnlyContain(c => c.StudioId == tenantA);
    }

    private async Task<Guid> SeedArtist(Guid tenantId, Guid userId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Artist artist = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "A",
            LastName = "B",
            Email = $"{Guid.NewGuid()}@a.com",
        };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedCharge(Guid tenantId, Guid artistId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        BoothRentSchedule schedule = new()
        {
            StudioId = tenantId,
            ArtistId = artistId,
            AmountFixed = 50m,
            Frequency = Domain.Enums.RentFrequency.Weekly,
            NextChargeDate = DateTime.UtcNow.AddDays(7),
            IsActive = true,
        };
        ctx.BoothRentSchedules.Add(schedule);
        await ctx.SaveChangesAsync();

        BoothRentCharge charge = new()
        {
            StudioId = tenantId,
            BoothRentScheduleId = schedule.Id,
            ArtistId = artistId,
            Amount = 50m,
            ChargedDate = DateTime.UtcNow,
            IsSettled = false,
        };
        ctx.BoothRentCharges.Add(charge);
        await ctx.SaveChangesAsync();
        return charge.Id;
    }
}
