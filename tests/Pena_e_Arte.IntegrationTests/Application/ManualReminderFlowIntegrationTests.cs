using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Application.Reminders.Queries;
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

// Quota is Redis-backed (IManualReminderQuotaService) — CI provisions MySQL only, not Redis
// (see .github/workflows/ci.yml's comment: every external service is NSubstitute-mocked at
// the handler level), so quota enforcement itself is covered at the unit-test level
// (ManualReminderQuotaServiceTests) against a mocked IDatabase, not here against real Redis.
[Collection("Database")]
public class ManualReminderFlowIntegrationTests(DatabaseFixture fixture)
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobScheduler _jobs = CreateJobsMock();
    private readonly IManualReminderQuotaService _quota = Substitute.For<IManualReminderQuotaService>();

    private static IJobScheduler CreateJobsMock()
    {
        IJobScheduler jobs = Substitute.For<IJobScheduler>();
        jobs.ScheduleManualReminder(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()).Returns("hangfire-job-1");
        return jobs;
    }

    private CreateManualReminderHandler CreateCreateHandler(AppDbContext db, ICurrentTenant tenant) =>
        new(db, tenant, _currentUser, _jobs, _quota);

    private CancelManualReminderHandler CreateCancelHandler(AppDbContext db) =>
        new(db, _currentUser, _jobs);

    private static ICurrentTenant TenantFor(Guid studioId)
    {
        CurrentTenantService t = new();
        t.SetTenant(studioId);
        return t;
    }

    private async Task<Guid> SeedArtistAsCurrentUser(Guid studioId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Guid userId = Guid.NewGuid();
        Artist artist = new() { StudioId = studioId, UserId = userId, FirstName = "Jo", LastName = "Artist", Email = $"{Guid.NewGuid()}@a.com" };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(userId);
        return artist.Id;
    }

    [Fact]
    public async Task CreateManualReminder_RawContact_PersistsToDatabase()
    {
        Guid studioId = Guid.NewGuid();
        await SeedArtistAsCurrentUser(studioId);

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        CreateManualReminderRequest req = new(null, null, null, "Walk-in Wendy", "+351920000002", null, null);
        ManualReminderResponse result = await CreateCreateHandler(db, TenantFor(studioId))
            .Handle(new CreateManualReminderCommand(req), default);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        bool exists = await verify.ManualReminders.AnyAsync(m => m.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ManualReminder_CreatedInStudioA_NotVisibleFromStudioB()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        await SeedArtistAsCurrentUser(studioA);

        await using AppDbContext dbA = fixture.CreateDbContext(studioA);
        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        await CreateCreateHandler(dbA, TenantFor(studioA)).Handle(new CreateManualReminderCommand(req), default);

        await using AppDbContext dbB = fixture.CreateDbContext(studioB);
        List<ManualReminder> visibleFromB = await dbB.ManualReminders.ToListAsync();

        visibleFromB.Should().BeEmpty();
    }

    [Fact]
    public async Task ManualReminder_CreatedInStudioA_CannotBeCancelledFromStudioB()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        await SeedArtistAsCurrentUser(studioA);

        await using AppDbContext dbA = fixture.CreateDbContext(studioA);
        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        ManualReminderResponse created = await CreateCreateHandler(dbA, TenantFor(studioA))
            .Handle(new CreateManualReminderCommand(req), default);

        _currentUser.Role.Returns("owner");
        await using AppDbContext dbB = fixture.CreateDbContext(studioB);
        Func<Task> act = () => CreateCancelHandler(dbB).Handle(new CancelManualReminderCommand(created.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateThenGet_AppointmentLinked_ReturnsInHistory()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistId = await SeedArtistAsCurrentUser(studioId);

        await using AppDbContext seedCtx = fixture.CreateDbContext(studioId);
        Client client = new() { StudioId = studioId, ArtistId = artistId, FirstName = "Ana", LastName = "Silva", Email = $"{Guid.NewGuid()}@c.com", Phone = "+351910000001" };
        seedCtx.Clients.Add(client);
        await seedCtx.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId = studioId, ArtistId = artistId, ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(1), EndDate = DateTime.UtcNow.AddDays(1).AddHours(2),
            DurationMinutes = 120, Status = AppointmentStatus.Confirmed, DepositStatus = DepositStatus.Paid
        };
        seedCtx.Appointments.Add(appointment);
        await seedCtx.SaveChangesAsync();

        await using AppDbContext createDb = fixture.CreateDbContext(studioId);
        CreateManualReminderRequest req = new(appointment.Id, null, null, null, null, null, null);
        await CreateCreateHandler(createDb, TenantFor(studioId)).Handle(new CreateManualReminderCommand(req), default);

        await using AppDbContext queryDb = fixture.CreateDbContext(studioId);
        GetManualRemindersHandler queryHandler = new(queryDb);
        List<ManualReminderResponse> results = await queryHandler.Handle(
            new GetManualRemindersQuery(appointment.Id, null), default);

        results.Should().ContainSingle(r => r.RecipientName == "Ana Silva");
    }
}
