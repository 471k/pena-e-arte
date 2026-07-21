using FluentAssertions;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class GetRevenueSummaryHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Handle_CrossTenantPayments_OnlyIncludesCallingTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        (Guid artistA, Guid clientA, Guid apptA) = await SeedArtistAndAppointment(tenantA);
        (Guid artistB, Guid clientB, Guid apptB) = await SeedArtistAndAppointment(tenantB);

        await SeedPayment(tenantA, apptA, clientA, 100m);
        await SeedPayment(tenantB, apptB, clientB, 500m);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetRevenueSummaryHandler handler = new(db);
        RevenueSummaryResponse result = await handler.Handle(new GetRevenueSummaryQuery(), default);

        result.PerArtist.Should().ContainSingle(a => a.ArtistId == artistA && a.Revenue == 100m);
        result.PerArtist.Should().NotContain(a => a.ArtistId == artistB);
        result.MonthlyTrend.Sum(p => p.Revenue).Should().Be(100m);
    }

    [Fact]
    public async Task Handle_PartiallyRefundedPayment_RetainsPartialRevenueThroughRealDatabaseRoundTrip()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid artistId, Guid clientId, Guid apptId) = await SeedArtistAndAppointment(tenantId);
        await SeedPayment(tenantId, apptId, clientId, 100m, refundedAmount: 40m);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetRevenueSummaryHandler handler = new(db);
        RevenueSummaryResponse result = await handler.Handle(new GetRevenueSummaryQuery(), default);

        result.PerArtist.Should().ContainSingle(a => a.ArtistId == artistId && a.Revenue == 60m);
        result.MonthlyTrend.Sum(p => p.Revenue).Should().Be(60m);
    }

    private async Task<(Guid ArtistId, Guid ClientId, Guid AppointmentId)> SeedArtistAndAppointment(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Artist artist = new()
        {
            StudioId = tenantId, FirstName = "A", LastName = "B",
            Email = $"{Guid.NewGuid():N}@a.com",
        };
        Client client = new()
        {
            StudioId = tenantId, FirstName = "C", LastName = "D",
            Email = $"{Guid.NewGuid():N}@c.com",
        };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        Appointment appt = new()
        {
            StudioId        = tenantId,
            ArtistId        = artist.Id,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(1),
            EndDate         = DateTime.UtcNow.AddDays(1).AddHours(1),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Completed,
            DepositStatus   = DepositStatus.Paid,
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();

        return (artist.Id, client.Id, appt.Id);
    }

    private async Task SeedPayment(
        Guid tenantId, Guid appointmentId, Guid clientId, decimal amount, decimal? refundedAmount = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Payments.Add(new Payment
        {
            StudioId       = tenantId,
            AppointmentId  = appointmentId,
            ClientId       = clientId,
            Amount         = amount,
            Status         = refundedAmount is null ? PaymentStatus.Paid : PaymentStatus.Refunded,
            Method         = ClientPaymentMethod.Card,
            PaidAt         = DateTime.UtcNow,
            RefundedAmount = refundedAmount,
        });
        await ctx.SaveChangesAsync();
    }
}
