using FluentAssertions;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

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
    public async Task Handle_PaymentsHaveSplits_IncludesSplitsInResponse()
    {
        Guid paymentId = await SeedPayment(200m);
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "Deposit",
            Amount    = 200m
        });
        await _db.SaveChangesAsync();

        List<PaymentResponse> result = await CreateSut()
            .Handle(new GetPaymentsQuery(), default);

        result.Single().SessionSplits.Should().ContainSingle(s => s.Label == "Deposit");
    }

    private async Task SeedPayments(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await SeedPayment(100m * (i + 1));
            await Task.Delay(1); // ensure distinct CreatedAt for cursor pagination
        }
    }

    private async Task<Guid> SeedPayment(decimal amount)
    {
        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId      = Guid.NewGuid(),
            Amount        = amount,
            Status        = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
