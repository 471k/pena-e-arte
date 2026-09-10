using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Packages.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PackageHandlerIntegrationTests(DatabaseFixture fixture)
{
    private readonly ISlotLocker _locker = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    [Fact]
    public async Task PurchasePackage_ThenReconciliationConfirms_GrantsSessions()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        (Guid clientId, Guid packageId) = await SeedClientAndPackage(tenantId, userId, sessionCount: 5);

        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        IPaymentProvider provider = Substitute.For<IPaymentProvider>();
        provider.CreatePaymentHoldAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(("pi_pkg_test", "secret_pkg_test"));

        ICurrentUser clientUser = Substitute.For<ICurrentUser>();
        clientUser.Role.Returns("client");
        clientUser.UserId.Returns(userId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        PurchasePackageHandler handler = new(db, TenantFor(tenantId), clientUser, provider);

        PurchasePackageResponse result = await handler.Handle(
            new PurchasePackageCommand(new PurchasePackageRequest(packageId)), default);

        await using AppDbContext verify1 = fixture.CreateDbContext(tenantId);
        PackagePurchase purchase = await verify1.PackagePurchases.FirstAsync(p => p.Id == result.PackagePurchaseId);
        purchase.SessionsRemaining.Should().Be(0);
        purchase.ConfirmedAt.Should().BeNull();

        provider.GetStatusAsync("pi_pkg_test", Arg.Any<CancellationToken>()).Returns("succeeded");

        await using AppDbContext reconcileDb = fixture.CreateDbContext(Guid.Empty);
        PackagePurchaseReconciliationJob job = new(reconcileDb, provider);
        await job.RunAsync();

        await using AppDbContext verify2 = fixture.CreateDbContext(tenantId);
        PackagePurchase confirmed = await verify2.PackagePurchases.FirstAsync(p => p.Id == result.PackagePurchaseId);
        confirmed.SessionsRemaining.Should().Be(5);
        confirmed.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAppointment_WithConfirmedPackage_ProducesZeroDepositAndPrePaidStatus()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        (Guid clientId, Guid packageId) = await SeedClientAndPackage(tenantId, userId, sessionCount: 3);
        Guid artistId = await SeedArtist(tenantId);
        Guid purchaseId = await SeedConfirmedPurchase(tenantId, clientId, packageId, sessionsRemaining: 3);

        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);

        ICurrentUser clientUser = Substitute.For<ICurrentUser>();
        clientUser.Role.Returns("client");
        clientUser.UserId.Returns(userId);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CreateAppointmentHandler handler = new(db, TenantFor(tenantId), clientUser, _locker, _jobs, _realtime, _sender, _planLimits);

        AppointmentResponse result = await handler.Handle(new CreateAppointmentCommand(
            new CreateAppointmentRequest(artistId, clientId, DateTime.UtcNow.AddDays(3), 90, null,
                PackagePurchaseId: purchaseId)), default);

        result.DepositAmount.Should().Be(0m);
        result.DepositStatus.Should().Be(DepositStatus.PrePaid.ToString());

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        PackagePurchase purchase = await verify.PackagePurchases.FirstAsync(p => p.Id == purchaseId);
        purchase.SessionsRemaining.Should().Be(2);
    }

    private async Task<(Guid ClientId, Guid PackageId)> SeedClientAndPackage(Guid tenantId, Guid userId, int sessionCount)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new() { StudioId = tenantId, UserId = userId, FirstName = "C", LastName = "L", Email = $"{Guid.NewGuid()}@c.com" };
        Package package = new() { StudioId = tenantId, Name = "Pack", SessionCount = sessionCount, Price = 300m, IsActive = true };
        ctx.Clients.Add(client);
        ctx.Packages.Add(package);
        await ctx.SaveChangesAsync();
        return (client.Id, package.Id);
    }

    private async Task<Guid> SeedArtist(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            ctx.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = tenantId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            ctx.StudioHours.Add(new StudioHours
            {
                StudioId = tenantId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsOpen = true,
            });
        }
        await ctx.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedConfirmedPurchase(Guid tenantId, Guid clientId, Guid packageId, int sessionsRemaining)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        PackagePurchase purchase = new()
        {
            StudioId = tenantId,
            PackageId = packageId,
            ClientId = clientId,
            SessionsRemaining = sessionsRemaining,
            ProviderReferenceId = "pi_confirmed",
            Provider = "pok",
            ConfirmedAt = DateTime.UtcNow,
        };
        ctx.PackagePurchases.Add(purchase);
        await ctx.SaveChangesAsync();
        return purchase.Id;
    }

    private static ICurrentTenant TenantFor(Guid tenantId)
    {
        CurrentTenantService t = new();
        t.SetTenant(tenantId);
        return t;
    }
}
