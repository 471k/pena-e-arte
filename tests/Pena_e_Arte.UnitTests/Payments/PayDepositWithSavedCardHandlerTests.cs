using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class PayDepositWithSavedCardHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly IPokCardTokenService _cardTokens = Substitute.For<IPokCardTokenService>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();

    public PayDepositWithSavedCardHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.UserId.Returns(_clientUserId);
        _currentUser.Role.Returns("client");
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true, SupportedCurrencies: ["ALL", "EUR"],
            SavedCardsEnabled: true));
        _provider.CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>())
               .Returns(("order-new", "order-new"));
        _cardTokens.SetupTokenizedThreeDsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new PokPayerAuthSetup("ref-1", new PokDeviceDataCollection("https://ddc.example.com", "ddc-token")));

        _db.Studios.Add(new Studio { Id = _studioId, Name = "Test", Slug = "test", CountryCode = "AL", Currency = "ALL" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private PayDepositWithSavedCardHandler CreateSut() => new(_db, _tenant, _currentUser, _provider, _cardTokens, _jobs);

    [Fact]
    public async Task Handle_SavedCardsSwitchedOff_RefusesBeforeAnyHoldOrPokCall()
    {
        // C-9: with Payments:SavedCardsEnabled off nothing is created, resumed or sent to POK.
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true, SupportedCurrencies: ["ALL", "EUR"],
            SavedCardsEnabled: false));
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 80m);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");

        Func<Task> act = () => CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage(SavedCardGuard.SavedCardsSwitchedOffMessage);
        _db.Payments.Should().BeEmpty();
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        _cardTokens.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_HappyPath_MintsHoldThenSetsUpThreeDs()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 80m);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");

        PayWithSavedCardSetupResponse result = await CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        result.Status.Should().Be(PaymentStatus.Pending.ToString());
        result.OrderId.Should().Be("order-new");
        result.CardTokenId.Should().Be("card-abc");
        result.PayerAuthSetupReferenceId.Should().Be("ref-1");
        result.DeviceDataCollection!.Url.Should().Be("https://ddc.example.com");

        await _cardTokens.Received(1).SetupTokenizedThreeDsAsync(_studioId, "order-new", "card-abc", Arg.Any<CancellationToken>());
        _db.Payments.Should().ContainSingle(p => p.AppointmentId == appointmentId && p.Status == PaymentStatus.Pending);
    }

    [Fact]
    public async Task Handle_ExistingPendingHold_ResumesOrderThenSetsUpThreeDsAgainstIt()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending, "order-old", "order-old");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "order-old", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PayWithSavedCardSetupResponse result = await CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        result.OrderId.Should().Be("order-old");
        await _provider.DidNotReceive().CreatePaymentHoldAsync(Arg.Any<PaymentHoldRequest>(), Arg.Any<CancellationToken>());
        await _cardTokens.Received(1).SetupTokenizedThreeDsAsync(_studioId, "order-old", "card-abc", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyCaptured_SkipsThreeDsSetupAndReportsStatusOnly()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending, "order-held", "order-held");
        _provider.GetStatusAsync(Arg.Any<Guid>(), "order-held", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        PayWithSavedCardSetupResponse result = await CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
        result.OrderId.Should().BeNull();
        result.PayerAuthSetupReferenceId.Should().BeNull();
        result.DeviceDataCollection.Should().BeNull();
        await _cardTokens.DidNotReceiveWithAnyArgs().SetupTokenizedThreeDsAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Handle_SavedMethodBelongingToAnotherClient_ThrowsNotFoundException()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid othersMethodId = await SeedSavedMethod(Guid.NewGuid(), "not-yours");

        Func<Task> act = () => CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, othersMethodId)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NonExistentSavedMethod_ThrowsNotFoundException()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);

        Func<Task> act = () => CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AppointmentOfAnotherClient_ThrowsNotFoundException()
    {
        Guid otherClientId = await SeedClient(Guid.NewGuid());
        Guid appointmentId = await SeedAppointment(otherClientId);
        Guid methodId = await SeedSavedMethod(otherClientId, "card-abc");

        Func<Task> act = () => CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoDepositRequired_ThrowsBusinessRuleViolation()
    {
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId, depositAmount: 0m);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");

        Func<Task> act = () => CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PendingHoldNearExpiry_CancelsAndMintsFreshHoldThenSetsUpThreeDs()
    {
        // The saved-card flow shares CreateDepositPaymentCommand's ResolveOrCreateHoldAsync, so it
        // never sets up 3DS against an order that is about to expire at POK.
        Guid clientId = await SeedClient(_clientUserId);
        Guid appointmentId = await SeedAppointment(clientId);
        Guid methodId = await SeedSavedMethod(clientId, "card-abc");
        await SeedPayment(appointmentId, ClientPaymentMethod.Card, PaymentStatus.Pending, "order-expiring", "order-expiring",
            holdExpiresAt: DateTime.UtcNow.AddMinutes(5));
        _provider.GetStatusAsync(Arg.Any<Guid>(), "order-expiring", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        PayWithSavedCardSetupResponse result = await CreateSut()
            .Handle(new PayDepositWithSavedCardCommand(new PayWithSavedCardRequest(appointmentId, methodId)), default);

        await _provider.Received(1).CancelAsync(_studioId, "order-expiring", Arg.Any<CancellationToken>());
        result.OrderId.Should().Be("order-new");
        await _cardTokens.Received(1).SetupTokenizedThreeDsAsync(_studioId, "order-new", "card-abc", Arg.Any<CancellationToken>());
        await _cardTokens.DidNotReceive().SetupTokenizedThreeDsAsync(
            Arg.Any<Guid>(), "order-expiring", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private async Task<Guid> SeedClient(Guid userId)
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
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId, decimal depositAmount = 50m)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = clientId,
            Date = DateTime.UtcNow.AddDays(5),
            EndDate = DateTime.UtcNow.AddDays(5).AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = depositAmount,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task<Guid> SeedSavedMethod(Guid clientId, string providerCardTokenId)
    {
        SavedPaymentMethod method = new() { StudioId = _studioId, ClientId = clientId, ProviderCardTokenId = providerCardTokenId };
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return method.Id;
    }

    private async Task<Guid> SeedPayment(
        Guid appointmentId, ClientPaymentMethod method, PaymentStatus status, string? intentId, string? clientToken,
        DateTime? holdExpiresAt = null)
    {
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = _clientUserId,
            Amount = 50m,
            Method = method,
            Status = status,
            ProviderReferenceId = intentId,
            ClientToken = clientToken,
            HoldExpiresAt = holdExpiresAt,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment.Id;
    }
}
