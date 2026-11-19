using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentClientTokenHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetPaymentClientTokenHandler CreateSut() => new(_db, _user, _jobs);

    [Fact]
    public async Task Handle_OwnerRole_ReturnsToken()
    {
        Guid paymentId = await SeedPayment("pi_test_token_abc");
        _user.Role.Returns("owner");

        PaymentClientTokenResponse result = await CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ClientToken.Should().Be("pi_test_token_abc");
    }

    [Fact]
    public async Task Handle_ClientRole_MatchingClient_ReturnsToken()
    {
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(userId);

        Guid paymentId = await SeedPayment("pi_token_client", clientId: clientId);
        _user.Role.Returns("client");
        _user.UserId.Returns(userId);

        PaymentClientTokenResponse result = await CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ClientToken.Should().Be("pi_token_client");
    }

    [Fact]
    public async Task Handle_ClientRole_DifferentClient_ThrowsUnauthorized()
    {
        Guid paymentId = await SeedPayment("pi_token_other");
        _user.Role.Returns("client");
        _user.UserId.Returns(Guid.NewGuid()); // not linked to any client

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Handle_UnknownPayment_ThrowsNotFound()
    {
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientTokenQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MissingClientToken_ThrowsNotFound()
    {
        Guid paymentId = await SeedPayment(null);
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ReturnsTheFrozenLekAndTheStudioCountry()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s", CountryCode = "AL", Currency = "EUR" });
        Guid paymentId = await SeedPayment("order_ref");
        Payment payment = _db.Payments.Single(p => p.Id == paymentId);
        payment.Currency = "EUR";
        payment.ReferenceAmount = 4537m;
        payment.ReferenceCurrency = "ALL";
        await _db.SaveChangesAsync();
        _user.Role.Returns("owner");

        PaymentClientTokenResponse result = await CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ReferenceAmount.Should().Be(4537m);
        result.ReferenceCurrency.Should().Be("ALL");
        result.StudioCountryCode.Should().Be("AL");
    }

    [Fact]
    public async Task Handle_NoReference_LeavesTheLekFieldsNull()
    {
        Guid paymentId = await SeedPayment("order_plain");
        _user.Role.Returns("owner");

        PaymentClientTokenResponse result = await CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ReferenceAmount.Should().BeNull();
        result.ReferenceCurrency.Should().BeNull();
    }

    // ── Finding A: no token for money that is no longer owed ─────────────────────────────

    [Fact]
    public async Task Handle_ActiveAppointment_PendingCardPayment_ReturnsToken()
    {
        // The live path. The guard's failure mode is refusing everybody, so this is the one that matters.
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(userId);
        Guid paymentId = await SeedPayment("order_live", clientId: clientId,
            appointmentStatus: AppointmentStatus.Confirmed, providerReferenceId: "order_live");
        _user.Role.Returns("client");
        _user.UserId.Returns(userId);

        PaymentClientTokenResponse result = await CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ClientToken.Should().Be("order_live");
        _jobs.DidNotReceive().TriggerPaymentReconciliationNow();
    }

    [Fact]
    public async Task Handle_ActiveAppointment_CapturedPayment_ReturnsToken()
    {
        Guid paymentId = await SeedPayment("order_held", status: PaymentStatus.Captured,
            appointmentStatus: AppointmentStatus.Confirmed);
        _user.Role.Returns("owner");

        PaymentClientTokenResponse result = await CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        result.ClientToken.Should().Be("order_held");
    }

    [Fact]
    public async Task Handle_CancelledAppointment_RefusesWithoutAToken()
    {
        Guid userId = Guid.NewGuid();
        Guid clientId = await SeedClient(userId);
        Guid paymentId = await SeedPayment("order_dead", clientId: clientId,
            appointmentStatus: AppointmentStatus.Cancelled);
        _user.Role.Returns("client");
        _user.UserId.Returns(userId);

        PaymentClientTokenResponse? result = null;
        Func<Task> act = async () => result = await CreateSut()
            .Handle(new GetPaymentClientTokenQuery(paymentId), default);

        // BusinessRuleViolationException is 422 (ExceptionMiddleware); its message is what the page shows.
        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .WithMessage("This appointment was cancelled, so there is nothing to pay.");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CancelledAppointment_CardPaymentInFlight_TriggersReconciliationOnce()
    {
        Guid paymentId = await SeedPayment("order_inflight", appointmentStatus: AppointmentStatus.Cancelled,
            providerReferenceId: "order_inflight");
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _jobs.Received(1).TriggerPaymentReconciliationNow();
    }

    [Theory]
    [InlineData(ClientPaymentMethod.Cash, PaymentStatus.CashPending, "order_cash")]
    [InlineData(ClientPaymentMethod.Card, PaymentStatus.Pending, null)]          // never reached the provider
    [InlineData(ClientPaymentMethod.Card, PaymentStatus.Captured, "order_held")] // not Pending: same predicate as #356
    public async Task Handle_CancelledAppointment_NotACardPaymentInFlight_DoesNotTriggerReconciliation(
        ClientPaymentMethod method, PaymentStatus status, string? providerReferenceId)
    {
        Guid paymentId = await SeedPayment("order_x", status: status, method: method,
            appointmentStatus: AppointmentStatus.Cancelled, providerReferenceId: providerReferenceId);
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _jobs.DidNotReceive().TriggerPaymentReconciliationNow();
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, "This payment has already been settled.")]
    [InlineData(PaymentStatus.Refunded, "This payment has already been settled.")]
    // Not "settled": the order behind the token is dead, and My Bookings mints a fresh one on this row.
    [InlineData(PaymentStatus.Failed, "This card payment can no longer be completed.*\"Pay deposit\"*My Bookings*")]
    // Not "settled" either: nothing is paid yet, the client chose cash.
    [InlineData(PaymentStatus.CashPending, "This deposit is being paid in cash at the studio.*\"Pay by card instead\"*")]
    public async Task Handle_ActiveAppointment_NonPayableStatus_Refuses(PaymentStatus status, string message)
    {
        Guid paymentId = await SeedPayment("order_settled", status: status,
            appointmentStatus: AppointmentStatus.Confirmed);
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).WithMessage(message);
    }

    [Fact]
    public async Task Handle_NonOwningClient_CancelledAppointment_GetsUnauthorized_NotTheRefusal()
    {
        // Ownership runs before the guard: a stranger learns nothing about the payment's state.
        Guid paymentId = await SeedPayment("order_other", appointmentStatus: AppointmentStatus.Cancelled,
            providerReferenceId: "order_other");
        Guid strangerUserId = Guid.NewGuid();
        await SeedClient(strangerUserId);
        _user.Role.Returns("client");
        _user.UserId.Returns(strangerUserId);

        Func<Task> act = () => CreateSut().Handle(new GetPaymentClientTokenQuery(paymentId), default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _jobs.DidNotReceive().TriggerPaymentReconciliationNow();
    }

    [Fact]
    public async Task Handle_AppointmentNotVisible_ThrowsNotFound()
    {
        // Payment.AppointmentId is a required FK, so in production this is a soft-deleted appointment.
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Status = PaymentStatus.Pending,
            ClientToken = "order_orphan"
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _user.Role.Returns("owner");

        Func<Task> act = () => CreateSut().Handle(new GetPaymentClientTokenQuery(payment.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedClient(Guid userId)
    {
        Client client = new() { StudioId = _studioId, UserId = userId, Email = $"{userId:N}@test.com" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedPayment(
        string? token,
        Guid? clientId = null,
        PaymentStatus status = PaymentStatus.Pending,
        ClientPaymentMethod method = ClientPaymentMethod.Card,
        AppointmentStatus appointmentStatus = AppointmentStatus.Confirmed,
        string? providerReferenceId = null)
    {
        Guid resolvedClientId = clientId ?? Guid.NewGuid();
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = resolvedClientId,
            Date = new DateTime(2026, 12, 10, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 10, 11, 30, 0, DateTimeKind.Utc),
            DurationMinutes = 90,
            Status = appointmentStatus,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appointment);

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointment.Id,
            ClientId = resolvedClientId,
            Amount = 50m,
            Status = status,
            Method = method,
            ClientToken = token,
            ProviderReferenceId = providerReferenceId
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
