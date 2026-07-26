using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentInvoiceHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPaymentInvoiceService _invoiceService = Substitute.For<IPaymentInvoiceService>();
    private readonly Guid _studioId = Guid.NewGuid();

    private static readonly byte[] FakePdfBytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF"

    public GetPaymentInvoiceHandlerTests()
    {
        _currentUser.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);
        _invoiceService.Generate(Arg.Any<PaymentInvoiceData>()).Returns(FakePdfBytes);
    }

    private GetPaymentInvoiceHandler CreateSut() =>
        new(_db, _currentUser, _tenant, _invoiceService);

    [Fact]
    public async Task Handle_OwnerRole_ReturnsInvoiceBytes()
    {
        Guid paymentId = await SeedPayment();

        byte[] result = await CreateSut().Handle(new GetPaymentInvoiceQuery(paymentId), default);

        result.Should().Equal(FakePdfBytes);
        _invoiceService.Received(1).Generate(Arg.Any<PaymentInvoiceData>());
    }

    [Fact]
    public async Task Handle_ClientRole_OwnPayment_ReturnsInvoiceBytes()
    {
        Guid userId = Guid.NewGuid();
        Guid paymentId = await SeedPayment(userId);
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(userId);

        byte[] result = await CreateSut().Handle(new GetPaymentInvoiceQuery(paymentId), default);

        result.Should().Equal(FakePdfBytes);
    }

    [Fact]
    public async Task Handle_ClientRole_OtherClientPayment_ThrowsNotFoundException()
    {
        Guid paymentId = await SeedPayment();
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(Guid.NewGuid()); // different user

        Func<Task> act = () => CreateSut().Handle(new GetPaymentInvoiceQuery(paymentId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetPaymentInvoiceQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InvoiceDataContainsCorrectAmount()
    {
        Guid paymentId = await SeedPayment(amount: 150m);

        await CreateSut().Handle(new GetPaymentInvoiceQuery(paymentId), default);

        _invoiceService.Received(1).Generate(
            Arg.Is<PaymentInvoiceData>(d => d.TotalAmount == 150m));
    }

    private async Task<Guid> SeedPayment(Guid? userId = null, decimal amount = 100m)
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId = client.Id,
            Amount = amount,
            Status = PaymentStatus.Paid,
            PaidAt = DateTime.UtcNow,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment.Id;
    }
}
