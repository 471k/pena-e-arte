using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E04b (A04) on real MySQL: the no-show preview and the real no-show share one decision (NoShowOutcome)
/// and one payment lookup, so what the artist is shown before confirming is what then happens, for every
/// deposit state, including a payment that changes between preview and commit (the commit wins).
/// Declared cash is closed as nothing collected, as cancellation closes it (Phi, 2026-11-05, §2.1).
/// </summary>
[Collection("Database")]
public class NoShowPreviewIntegrationTests(DatabaseFixture fixture)
{
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();

    public static TheoryData<string, PaymentStatus?, ClientPaymentMethod, string?, string> States => new()
    {
        { "no payment",          null,                      ClientPaymentMethod.Card, null,       "NothingCollected" },
        { "unpaid card intent",  PaymentStatus.Pending,     ClientPaymentMethod.Card, "pok_1",    "NothingCollected" },
        { "failed card",         PaymentStatus.Failed,      ClientPaymentMethod.Card, "pok_1",    "NothingCollected" },
        { "charged card",        PaymentStatus.Paid,        ClientPaymentMethod.Card, "pok_1",    "ChargedDepositKept" },
        { "cash received",       PaymentStatus.Paid,        ClientPaymentMethod.Cash, null,       "CashDepositKept" },
        { "legacy held card",    PaymentStatus.Captured,    ClientPaymentMethod.Card, "pok_held", "HeldDepositCaptured" },
        { "declared cash",       PaymentStatus.CashPending, ClientPaymentMethod.Cash, null,       "DeclaredCashNotCollected" },
    };

    [Theory]
    [MemberData(nameof(States))]
    public async Task ThePreview_EqualsWhatTheNoShowDoes(
        string label, PaymentStatus? status, ClientPaymentMethod method, string? reference, string expected)
    {
        Seeded s = await Seed();
        if (status is { } st) await SeedPayment(s, st, method, reference);

        NoShowPreviewResponse preview = await Preview(s);
        NoShowResultResponse result = await Commit(s);

        preview.Outcome.Should().Be(expected, label);
        result.Outcome.Should().Be(preview.Outcome);
        result.Amount.Should().Be(preview.Amount);
        result.Currency.Should().Be(preview.Currency).And.Be("EUR");

        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        Appointment appointment = await db.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        appointment.DepositStatus.Should().Be(DepositStatus.Forfeited);

        Payment? payment = await db.Payments.SingleOrDefaultAsync(p => p.AppointmentId == s.AppointmentId);
        switch (expected)
        {
            case "HeldDepositCaptured":
                await _provider.Received(1).CaptureAsync(s.StudioId, "pok_held", Arg.Any<CancellationToken>());
                payment!.Status.Should().Be(PaymentStatus.Paid);
                payment.PaidAt.Should().NotBeNull();
                break;
            case "DeclaredCashNotCollected":
                // Phi, 2026-11-05: no cash was received, so nothing is recorded as received.
                payment!.Status.Should().Be(PaymentStatus.Refunded);
                payment.PaidAt.Should().BeNull();
                await _provider.DidNotReceiveWithAnyArgs().CaptureAsync(default, default!, default);
                break;
            default:
                await _provider.DidNotReceiveWithAnyArgs().CaptureAsync(default, default!, default);
                if (status is { } unchanged) payment!.Status.Should().Be(unchanged);
                break;
        }
    }

    [Fact]
    public async Task APaymentThatChangesAfterThePreview_TheCommittedResultWins()
    {
        Seeded s = await Seed();
        await SeedPayment(s, PaymentStatus.CashPending, ClientPaymentMethod.Cash, null);

        NoShowPreviewResponse preview = await Preview(s);
        preview.Outcome.Should().Be("DeclaredCashNotCollected");

        // The studio confirms the cash before the artist confirms the no-show.
        await using (AppDbContext edit = fixture.CreateDbContext(s.StudioId))
        {
            Payment p = await edit.Payments.SingleAsync(x => x.AppointmentId == s.AppointmentId);
            p.Status = PaymentStatus.Paid;
            p.PaidAt = DateTime.UtcNow;
            await edit.SaveChangesAsync();
        }

        NoShowResultResponse result = await Commit(s);

        result.Outcome.Should().Be("CashDepositKept", "the commit decides again and reports what it actually did");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<NoShowPreviewResponse> Preview(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetNoShowPreviewHandler(db, Owner()).Handle(new GetNoShowPreviewQuery(s.AppointmentId), default);
    }

    private async Task<NoShowResultResponse> Commit(Seeded s)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new MarkNoShowHandler(db, tenant, Substitute.For<IRealtimeNotifier>(), _provider, Owner())
            .Handle(new MarkNoShowCommand(s.AppointmentId), default);
    }

    private static ICurrentUser Owner()
    {
        ICurrentUser u = Substitute.For<ICurrentUser>();
        u.Role.Returns("owner");
        u.UserId.Returns(Guid.NewGuid());
        return u;
    }

    private sealed record Seeded(Guid StudioId, Guid AppointmentId, Guid ClientId);

    private async Task<Seeded> Seed()
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "NoShow", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = studioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = studioId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        DateTime start = DateTime.UtcNow.AddHours(-2);
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 80m,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();
        return new Seeded(studioId, appointment.Id, client.Id);
    }

    private async Task SeedPayment(Seeded s, PaymentStatus status, ClientPaymentMethod method, string? reference)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(s.StudioId);
        ctx.Payments.Add(new Payment
        {
            StudioId = s.StudioId,
            AppointmentId = s.AppointmentId,
            ClientId = s.ClientId,
            Amount = 80m,
            Currency = "EUR",
            Status = status,
            Method = method,
            ProviderReferenceId = reference,
            PaidAt = status == PaymentStatus.Paid ? DateTime.UtcNow.AddDays(-3) : null,
        });
        await ctx.SaveChangesAsync();
    }
}
