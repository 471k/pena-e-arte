using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class MarkPaymentAuthorizedHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    private MarkPaymentAuthorizedHandler CreateSut() => new(_db, _realtime);

    [Fact]
    public async Task Handle_PendingPayment_SetsStatusCaptured()
    {
        string intentId = $"pi_{Guid.NewGuid():N}";
        await SeedPayment(intentId, PaymentStatus.Pending);

        await CreateSut().Handle(new MarkPaymentAuthorizedCommand(intentId), default);

        _db.Payments.Single(p => p.ProviderReferenceId == intentId)
            .Status.Should().Be(PaymentStatus.Captured);
    }

    [Theory]
    [InlineData(PaymentStatus.Captured)]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.Failed)]
    public async Task Handle_NonPendingPayment_DoesNotChangeStatus(PaymentStatus status)
    {
        string intentId = $"pi_{Guid.NewGuid():N}";
        await SeedPayment(intentId, status);

        await CreateSut().Handle(new MarkPaymentAuthorizedCommand(intentId), default);

        _db.Payments.Single(p => p.ProviderReferenceId == intentId)
            .Status.Should().Be(status);
    }

    [Fact]
    public async Task Handle_UnknownIntent_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new MarkPaymentAuthorizedCommand("pi_unknown"), default);

        await act.Should().NotThrowAsync();
    }

    private async Task SeedPayment(string intentId, PaymentStatus status)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Method = ClientPaymentMethod.Card,
            Status = status,
            ProviderReferenceId = intentId,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
