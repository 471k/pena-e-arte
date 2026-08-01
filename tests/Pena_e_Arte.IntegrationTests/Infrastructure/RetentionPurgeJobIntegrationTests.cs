using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class RetentionPurgeJobIntegrationTests(DatabaseFixture fixture)
{
    private static RetentionOptions Opts(int consentDays = 730, int grace = 30) =>
        new() { ConsentForms = consentDays, BodyMaps = 730, GracePeriodBeforeHardPurge = grace };

    [Fact]
    public async Task RetentionPurgeJob_SoftDeletesPastWindow_ThenHardPurgesWithR2Delete()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid _, Guid formId) = await SeedSignedConsentFormAsync(tenantId, signedDaysAgo: 800);

        IR2Service r2 = Substitute.For<IR2Service>();

        // Run 1 — past the 730-day retention window → soft-delete only, no R2 delete.
        await RunJobAsync(tenantId, r2, Opts());

        await using (AppDbContext verify = fixture.CreateDbContext(tenantId))
        {
            ConsentForm form = await verify.ConsentForms.IgnoreQueryFilters().FirstAsync(f => f.Id == formId);
            form.DeletedAt.Should().NotBeNull(because: "it is past the retention window");
        }
        await r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Simulate the grace window having elapsed.
        await using (AppDbContext backdate = fixture.CreateDbContext(tenantId))
        {
            ConsentForm form = await backdate.ConsentForms.IgnoreQueryFilters().FirstAsync(f => f.Id == formId);
            form.DeletedAt = DateTime.UtcNow.AddDays(-40);
            await backdate.SaveChangesAsync();
        }

        // Run 2 — past the 30-day grace window → hard purge + R2 delete.
        await RunJobAsync(tenantId, r2, Opts());

        await using (AppDbContext verify = fixture.CreateDbContext(tenantId))
        {
            bool exists = await verify.ConsentForms.IgnoreQueryFilters().AnyAsync(f => f.Id == formId);
            exists.Should().BeFalse(because: "it is past the grace window");
        }
        await r2.Received().DeleteAsync($"consent/{tenantId}/{formId}.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetentionPurgeJob_LeavesFreshConsentFormUntouched()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid _, Guid formId) = await SeedSignedConsentFormAsync(tenantId, signedDaysAgo: 5);
        IR2Service r2 = Substitute.For<IR2Service>();

        await RunJobAsync(tenantId, r2, Opts());

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        ConsentForm form = await verify.ConsentForms.IgnoreQueryFilters().FirstAsync(f => f.Id == formId);
        form.DeletedAt.Should().BeNull(because: "it is still inside the retention window");
    }

    [Fact]
    public async Task RequestDataErasure_SoftDeletesConsentAndProfile_WithDistinctAuditAction()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid formId) = await SeedSignedConsentFormAsync(tenantId, signedDaysAgo: 5);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        StubCurrentUser user = new(Guid.NewGuid(), "owner");

        RequestDataErasureHandler handler = new(db);
        AuditLogBehavior<RequestDataErasureCommand, Unit> behavior = new(db, user, tenant);
        RequestDataErasureCommand command = new(clientId);
        await behavior.Handle(command, ct => handler.Handle(command, ct), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        ConsentForm form = await verify.ConsentForms.IgnoreQueryFilters().FirstAsync(f => f.Id == formId);
        form.DeletedAt.Should().NotBeNull(because: "erasure soft-deletes immediately");

        ClientProfile profile = await verify.ClientProfiles.IgnoreQueryFilters().FirstAsync(p => p.ClientId == clientId);
        profile.DeletedAt.Should().NotBeNull();

        AuditLogEntry entry = await verify.AuditLogEntries
            .FirstAsync(a => a.TargetId == clientId && a.StudioId == tenantId);
        entry.Action.Should().Be(AuditActions.ClientDataErasureRequested);
        entry.TargetType.Should().Be(AuditTargetTypes.Client);
        // Distinct from the automatic retention purge, which writes NO audit row at all.
        entry.Action.Should().NotBe("Retention.Purged");
    }

    [Fact]
    public async Task RequestMyDataErasure_ClientSelfService_SoftDeletesOwnData_AndAuditRecordsClientActor()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Guid clientId;
        Guid formId;
        await using (AppDbContext seed = fixture.CreateDbContext(tenantId))
        {
            Client client = new()
            {
                StudioId = tenantId,
                UserId = userId,
                FirstName = "Ana",
                LastName = "Costa",
                Email = $"{Guid.NewGuid()}@test.com",
            };
            Artist artist = new()
            {
                StudioId = tenantId,
                FirstName = "Art",
                LastName = "ist",
                Email = $"{Guid.NewGuid()}@test.com",
            };
            seed.Clients.Add(client);
            seed.Artists.Add(artist);
            await seed.SaveChangesAsync();

            Appointment appointment = new()
            {
                StudioId = tenantId,
                ClientId = client.Id,
                ArtistId = artist.Id,
                Date = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(-1).AddHours(2),
                DurationMinutes = 120,
            };
            seed.Appointments.Add(appointment);
            await seed.SaveChangesAsync();

            ConsentForm form = new()
            {
                StudioId = tenantId,
                ClientId = client.Id,
                AppointmentId = appointment.Id,
                SignedAt = DateTime.UtcNow,
            };
            seed.ConsentForms.Add(form);
            seed.ClientProfiles.Add(new ClientProfile { StudioId = tenantId, ClientId = client.Id });
            await seed.SaveChangesAsync();
            clientId = client.Id;
            formId = form.Id;
        }

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        StubCurrentUser user = new(userId, "client");

        RequestMyDataErasureHandler handler = new(db, user);
        AuditLogBehavior<RequestMyDataErasureCommand, Unit> behavior = new(db, user, tenant);
        RequestMyDataErasureCommand command = new();
        await behavior.Handle(command, ct => handler.Handle(command, ct), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        ConsentForm erased = await verify.ConsentForms.IgnoreQueryFilters().FirstAsync(f => f.Id == formId);
        erased.DeletedAt.Should().NotBeNull();

        AuditLogEntry entry = await verify.AuditLogEntries
            .FirstAsync(a => a.TargetId == clientId && a.StudioId == tenantId);
        entry.Action.Should().Be(AuditActions.ClientDataErasureRequested);
        entry.TargetType.Should().Be(AuditTargetTypes.Client);
        // Actor role distinguishes a client self-service erasure from an owner-initiated one.
        entry.ActorRole.Should().Be("client");
    }

    private async Task RunJobAsync(Guid tenantId, IR2Service r2, RetentionOptions opts)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        RetentionPurgeJob job = new(db, r2, Options.Create(opts), NullLogger<RetentionPurgeJob>.Instance);
        await job.RunAsync();
    }

    private async Task<(Guid clientId, Guid formId)> SeedSignedConsentFormAsync(Guid tenantId, int signedDaysAgo)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"{Guid.NewGuid()}@test.com"
        };
        Artist artist = new()
        {
            StudioId = tenantId,
            FirstName = "Art",
            LastName = "ist",
            Email = $"{Guid.NewGuid()}@test.com"
        };
        ctx.Clients.Add(client);
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            ArtistId = artist.Id,
            Date = DateTime.UtcNow.AddDays(-signedDaysAgo),
            EndDate = DateTime.UtcNow.AddDays(-signedDaysAgo).AddHours(2),
            DurationMinutes = 120
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();

        ConsentForm form = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            AppointmentId = appointment.Id,
            SignatureData = "sig",
            SignedAt = DateTime.UtcNow.AddDays(-signedDaysAgo),
            FileUrl = "https://cdn.example.com/consent/file.pdf",
        };
        ctx.ConsentForms.Add(form);

        ClientProfile profile = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            BodyMap = new BodyMap { Locations = [] }
        };
        ctx.ClientProfiles.Add(profile);
        await ctx.SaveChangesAsync();

        return (client.Id, form.Id);
    }

    private sealed record StubCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
