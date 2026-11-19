using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Finding A on real MySQL: the client token that /pay/{id} opens the card form with is refused once the
/// appointment is cancelled, under the same tenant-filtered reads production runs.
/// </summary>
[Collection("Database")]
public class ClientTokenGuardIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public ClientTokenGuardIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Client_CannotFetchATokenForACancelledAppointment()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        (Guid appointmentId, Guid paymentId) = await SeedOwnCardPayment(tenantId, userId);
        await using (AppDbContext ctx = _fixture.CreateDbContext(tenantId))
        {
            Appointment appt = await ctx.Appointments.SingleAsync(a => a.Id == appointmentId);
            appt.Status = AppointmentStatus.Cancelled;
            await ctx.SaveChangesAsync();
        }
        IJobScheduler jobs = Substitute.For<IJobScheduler>();

        Func<Task> act = () => RunAsClient(tenantId, userId, paymentId, jobs);

        await act.Should().ThrowAsync<BusinessRuleViolationException>().WithMessage("*cancelled*");
        jobs.Received(1).TriggerPaymentReconciliationNow();
    }

    [Fact]
    public async Task Client_StillGetsTheTokenForAnActiveAppointment()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        (_, Guid paymentId) = await SeedOwnCardPayment(tenantId, userId);

        PaymentClientTokenResponse result = await RunAsClient(tenantId, userId, paymentId, Substitute.For<IJobScheduler>());

        result.ClientToken.Should().StartWith("order_");
    }

    private async Task<(Guid AppointmentId, Guid PaymentId)> SeedOwnCardPayment(Guid tenantId, Guid userId)
    {
        await using AppDbContext ctx = _fixture.CreateDbContext(tenantId);
        ctx.Studios.Add(new Studio { Id = tenantId, Name = "Token Guard Studio", Slug = tenantId.ToString("N")[..8], CountryCode = "AL", Currency = "EUR" });
        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test" };
        Client client = new() { StudioId = tenantId, UserId = userId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        Appointment appt = new()
        {
            StudioId = tenantId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = new DateTime(2026, 12, 10, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 10, 11, 30, 0, DateTimeKind.Utc),
            DurationMinutes = 90,
            DepositAmount = 50m,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();

        string orderId = $"order_{Guid.NewGuid():N}";
        Payment payment = new()
        {
            StudioId = tenantId,
            AppointmentId = appt.Id,
            ClientId = client.Id,
            Amount = 50m,
            Currency = "EUR",
            Method = ClientPaymentMethod.Card,
            Status = PaymentStatus.Pending,
            ClientToken = orderId,
            ProviderReferenceId = orderId,
        };
        ctx.Payments.Add(payment);
        await ctx.SaveChangesAsync();
        return (appt.Id, payment.Id);
    }

    private async Task<PaymentClientTokenResponse> RunAsClient(Guid tenantId, Guid userId, Guid paymentId, IJobScheduler jobs)
    {
        await using AppDbContext db = _fixture.CreateDbContext(tenantId);
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.Role.Returns("client");
        currentUser.UserId.Returns(userId);
        GetPaymentClientTokenHandler handler = new(db, currentUser, jobs);
        return await handler.Handle(new GetPaymentClientTokenQuery(paymentId), default);
    }
}
