using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    public GetPaymentsHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private GetPaymentsHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoPayments_ReturnsEmptyList()
    {
        List<PaymentResponse> result = await CreateSut()
            .Handle(new GetPaymentsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultiplePayments_ReturnsAll()
    {
        await SeedPayments(3);

        List<PaymentResponse> result = await CreateSut()
            .Handle(new GetPaymentsQuery(), default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_PageSize_LimitsResults()
    {
        await SeedPayments(5);

        List<PaymentResponse> result = await CreateSut()
            .Handle(new GetPaymentsQuery(PageSize: 2), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithCursor_ReturnsSubsequentPage()
    {
        await SeedPayments(3);

        List<PaymentResponse> firstPage = await CreateSut()
            .Handle(new GetPaymentsQuery(PageSize: 1), default);

        List<PaymentResponse> secondPage = await CreateSut()
            .Handle(new GetPaymentsQuery(LastSeenId: firstPage[0].Id, PageSize: 10), default);

        secondPage.Should().HaveCount(2);
        secondPage.Should().NotContain(p => p.Id == firstPage[0].Id);
    }

    [Fact]
    public async Task Handle_WithCursor_SameCreatedAt_DoesNotSkipRecords()
    {
        DateTime sharedTimestamp = DateTime.UtcNow;
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();
        Guid id3 = Guid.NewGuid();

        // Id breaks the tie, newest-first means descending: sort the three GUIDs to know the page order.
        Guid[] sorted = [id1, id2, id3];
        Array.Sort(sorted, (a, b) => b.CompareTo(a));

        foreach (Guid id in sorted)
            await SeedPaymentWithId(id, sharedTimestamp);

        List<PaymentResponse> page1 = await CreateSut()
            .Handle(new GetPaymentsQuery(PageSize: 2), default);

        List<PaymentResponse> page2 = await CreateSut()
            .Handle(new GetPaymentsQuery(LastSeenId: page1[1].Id, PageSize: 10), default);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(1);
        page1.Select(p => p.Id).Should().Equal(sorted[0], sorted[1]);
        page2[0].Id.Should().Be(sorted[2]);
        page1.Select(p => p.Id).Concat(page2.Select(p => p.Id))
            .Should().BeEquivalentTo(sorted, because: "all three records must be returned across both pages");
    }

    // ── E05b-1: newest first ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NewestPaidFirst_UnpaidRowsByWhenTheyWereCreated()
    {
        DateTime now = DateTime.UtcNow;
        Guid paidLongAgo = await SeedPaymentWithId(Guid.NewGuid(), now.AddDays(-30), paidAt: now.AddDays(-20));
        Guid paidYesterday = await SeedPaymentWithId(Guid.NewGuid(), now.AddDays(-40), paidAt: now.AddDays(-1));
        Guid unpaidCreatedLastWeek = await SeedPaymentWithId(Guid.NewGuid(), now.AddDays(-7));

        List<PaymentResponse> result = await CreateSut().Handle(new GetPaymentsQuery(), default);

        // The sort key is PaidAt, falling back to CreatedAt: yesterday, last week, 20 days ago.
        result.Select(p => p.Id).Should().Equal(paidYesterday, unpaidCreatedLastWeek, paidLongAgo);
    }

    [Fact]
    public async Task Handle_CursorPagesWalkNewestFirstWithoutRepeats()
    {
        DateTime now = DateTime.UtcNow;
        List<Guid> expected = [];
        for (int i = 0; i < 7; i++)
            expected.Add(await SeedPaymentWithId(Guid.NewGuid(), now.AddDays(-100), paidAt: now.AddDays(-i)));

        List<Guid> seen = [];
        Guid? cursor = null;
        for (int page = 0; page < 5; page++)
        {
            List<PaymentResponse> rows = await CreateSut().Handle(new GetPaymentsQuery(cursor, PageSize: 3), default);
            if (rows.Count == 0) break;
            seen.AddRange(rows.Select(r => r.Id));
            cursor = rows[^1].Id;
        }

        seen.Should().Equal(expected, "each page carries on from the last row of the one before, newest first");
    }

    private async Task SeedPayments(int count)
    {
        for (int i = 0; i < count; i++)
            await SeedPayment(100m * (i + 1));
    }

    private Guid SeedAppointment(Guid clientId)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = clientId,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddMinutes(60),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    private async Task<Guid> SeedPayment(decimal amount)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = SeedAppointment(client.Id),
            ClientId = client.Id,
            Amount = amount,
            Status = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedPaymentWithId(Guid id, DateTime createdAt, DateTime? paidAt = null)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            Id = id,
            StudioId = _studioId,
            AppointmentId = SeedAppointment(client.Id),
            ClientId = client.Id,
            Amount = 100m,
            Status = paidAt is null ? PaymentStatus.Pending : PaymentStatus.Paid,
            PaidAt = paidAt,
            CreatedAt = createdAt
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return id;
    }
}
