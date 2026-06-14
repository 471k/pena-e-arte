using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class AppointmentHandlerIntegrationTests
{
    private readonly DatabaseFixture   _fixture;
    private readonly ICurrentUser      _user;
    private readonly ISlotLocker       _locker;
    private readonly IJobScheduler     _jobs;
    private readonly IRealtimeNotifier _realtime;
    private readonly ISender           _sender = Substitute.For<ISender>();

    public AppointmentHandlerIntegrationTests(DatabaseFixture fixture)
    {
        _fixture  = fixture;
        _user     = Substitute.For<ICurrentUser>();
        _locker   = Substitute.For<ISlotLocker>();
        _jobs     = Substitute.For<IJobScheduler>();
        _realtime = Substitute.For<IRealtimeNotifier>();

        _user.Role.Returns("artist");
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    // ── CreateAppointment ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAppointment_WithValidForeignKeys_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        CreateAppointmentRequest req = new(artistId, clientId, DateTime.UtcNow.AddDays(3), 90, null);
        AppointmentResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        bool exists = await verify.Appointments.AnyAsync(a => a.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAppointment_TimeOverlapWithRealSql_ThrowsSlotAlreadyBookedException()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(5);
        await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(120));

        // New appointment overlaps the existing one (starts 60 min into it)
        CreateAppointmentRequest req = new(artistId, clientId, start.AddMinutes(60), 90, null);

        Func<Task> act = () => RunCreateHandler(tenantId, req);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task CreateAppointment_CancelledOverlapWithRealSql_DoesNotThrow()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(6);
        await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(120),
                              AppointmentStatus.Cancelled);

        CreateAppointmentRequest req = new(artistId, clientId, start.AddMinutes(60), 90, null);

        Func<Task> act = () => RunCreateHandler(tenantId, req);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAppointment_DifferentArtistSameSlot_DoesNotThrow()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);
        (Guid artistId2, _)            = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(7);
        await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(120));

        // Same slot but different artist — no conflict
        CreateAppointmentRequest req = new(artistId2, clientId, start, 90, null);

        Func<Task> act = () => RunCreateHandler(tenantId, req);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAppointment_OverlapFromDifferentTenant_DoesNotThrow()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantA);

        DateTime start = DateTime.UtcNow.AddDays(8);
        await SeedAppointment(tenantA, artistId, clientId, start, start.AddMinutes(120));

        // tenantB creates artist/client with same IDs (impossible in prod but tests filter isolation)
        // Instead, create same-time appointment for tenantB's own artist
        (Guid b_artistId, Guid b_clientId) = await SeedArtistAndClient(tenantB);
        CreateAppointmentRequest req = new(b_artistId, b_clientId, start.AddMinutes(60), 90, null);

        Func<Task> act = () => RunCreateHandler(tenantB, req);

        await act.Should().NotThrowAsync();
    }

    // ── CancelAppointment ────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAppointment_PendingAppointment_SetsStatusCancelled()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(10);
        Guid apptId = await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(90));

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CancelAppointmentHandler handler = new(db, TenantFor(tenantId), _realtime, _sender);
        await handler.Handle(new CancelAppointmentCommand(apptId), default);

        await using AppDbContext verify = _fixture.CreateDbContext(tenantId);
        Appointment? appt = await verify.Appointments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == apptId);
        appt!.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAppointment_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CancelAppointmentHandler handler = new(db, TenantFor(tenantId), _realtime, _sender);

        Func<Task> act = () => handler.Handle(new CancelAppointmentCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CancelAppointment_CompletedAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId) = await SeedArtistAndClient(tenantId);

        DateTime start = DateTime.UtcNow.AddDays(11);
        Guid apptId = await SeedAppointment(tenantId, artistId, clientId, start, start.AddMinutes(90),
                                            AppointmentStatus.Completed);

        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CancelAppointmentHandler handler = new(db, TenantFor(tenantId), _realtime, _sender);

        Func<Task> act = () => handler.Handle(new CancelAppointmentCommand(apptId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<(Guid ArtistId, Guid ClientId)> SeedArtistAndClient(Guid tenantId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return (artist.Id, client.Id);
    }

    private async Task<Guid> SeedAppointment(
        Guid tenantId, Guid artistId, Guid clientId,
        DateTime start, DateTime end,
        AppointmentStatus status = AppointmentStatus.Pending)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        Appointment appt = new()
        {
            StudioId        = tenantId,
            ArtistId        = artistId,
            ClientId        = clientId,
            Date            = start,
            EndDate         = end,
            DurationMinutes = (int)(end - start).TotalMinutes,
            Status          = status,
            DepositStatus   = DepositStatus.Pending
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();
        return appt.Id;
    }

    private async Task<AppointmentResponse> RunCreateHandler(Guid tenantId, CreateAppointmentRequest req)
    {
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        CreateAppointmentHandler handler = new(db, TenantFor(tenantId), _user, _locker, _jobs, _realtime);
        return await handler.Handle(new CreateAppointmentCommand(req), default);
    }

    private static ICurrentTenant TenantFor(Guid tenantId)
    {
        CurrentTenantService t = new();
        t.SetTenant(tenantId);
        return t;
    }
}
