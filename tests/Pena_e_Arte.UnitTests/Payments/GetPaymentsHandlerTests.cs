using FluentAssertions;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetPaymentsHandler CreateSut() => new(_db);

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

        // Order by Id as tiebreaker — sort the three GUIDs to know expected page order
        Guid[] sorted = [id1, id2, id3];
        Array.Sort(sorted, (a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal));

        foreach (Guid id in sorted)
            await SeedPaymentWithId(id, sharedTimestamp);

        List<PaymentResponse> page1 = await CreateSut()
            .Handle(new GetPaymentsQuery(PageSize: 2), default);

        List<PaymentResponse> page2 = await CreateSut()
            .Handle(new GetPaymentsQuery(LastSeenId: page1[1].Id, PageSize: 10), default);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(1);
        page2[0].Id.Should().Be(sorted[2]);
        page1.Select(p => p.Id).Concat(page2.Select(p => p.Id))
            .Should().BeEquivalentTo(sorted, because: "all three records must be returned across both pages");
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

    private async Task SeedPaymentWithId(Guid id, DateTime createdAt)
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
            Status = PaymentStatus.Pending,
            CreatedAt = createdAt
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
    }
}
