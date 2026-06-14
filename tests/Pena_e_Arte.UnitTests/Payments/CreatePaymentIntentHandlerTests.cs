using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class CreatePaymentIntentHandlerTests
{
    private readonly FakeDbContext        _db           = FakeDbContext.Create();
    private readonly ICurrentTenant       _tenant       = Substitute.For<ICurrentTenant>();
    private readonly IStripePaymentService _stripe       = Substitute.For<IStripePaymentService>();
    private readonly IRealtimeNotifier    _realtime     = Substitute.For<IRealtimeNotifier>();
    private readonly Guid                 _studioId     = Guid.NewGuid();
    private readonly Guid                 _artistId     = Guid.NewGuid();
    private readonly Guid                 _clientId     = Guid.NewGuid();
    private readonly Guid                 _appointmentId = Guid.NewGuid();

    public CreatePaymentIntentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _stripe.CreatePaymentIntentAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(("pi_test_123", "pi_test_123_secret"));
    }

    private CreatePaymentIntentHandler CreateSut() =>
        new(_db, _tenant, _stripe, _realtime);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsClientSecret()
    {
        await SeedStudioAndAppointment();

        PaymentIntentResponse result = await CreateSut()
            .Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        result.ClientSecret.Should().Be("pi_test_123_secret");
        result.Status.Should().Be(PaymentStatus.Pending.ToString());
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsPaymentToDb()
    {
        await SeedStudioAndAppointment();

        await CreateSut().Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        _db.Payments.Should().ContainSingle(p =>
            p.AppointmentId == _appointmentId &&
            p.StudioId      == _studioId      &&
            p.Status        == PaymentStatus.Pending);
    }

    [Fact]
    public async Task Handle_ValidRequest_StoresStripeIntentId()
    {
        await SeedStudioAndAppointment();

        await CreateSut().Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        _db.Payments.Single().StripePaymentIntentId.Should().Be("pi_test_123");
    }

    [Fact]
    public async Task Handle_ValidRequest_NotifiesRealtime()
    {
        await SeedStudioAndAppointment();

        await CreateSut().Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "PaymentIntentCreated", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut()
            .Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DuplicateActivePayment_ThrowsBusinessRuleViolationException()
    {
        await SeedStudioAndAppointment();
        _db.Payments.Add(new Payment
        {
            StudioId      = _studioId,
            AppointmentId = _appointmentId,
            ClientId      = _clientId,
            Amount        = 200m,
            Status        = PaymentStatus.Pending
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_FailedPaymentExists_AllowsNewPayment()
    {
        await SeedStudioAndAppointment();
        _db.Payments.Add(new Payment
        {
            StudioId      = _studioId,
            AppointmentId = _appointmentId,
            ClientId      = _clientId,
            Amount        = 200m,
            Status        = PaymentStatus.Failed
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreatePaymentIntentCommand(ValidRequest()), default);

        await act.Should().NotThrowAsync();
    }

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio
        {
            Id   = _studioId,
            Name = "Test Studio",
            Slug = "test"
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedStudioAndAppointment()
    {
        await SeedStudio();

        _db.Artists.Add(new Artist { Id = _artistId, StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.com" });
        _db.Clients.Add(new Client { Id = _clientId, StudioId = _studioId, FirstName = "C", LastName = "D", Email = "c@d.com" });
        _db.Appointments.Add(new Appointment
        {
            Id              = _appointmentId,
            StudioId        = _studioId,
            ArtistId        = _artistId,
            ClientId        = _clientId,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddMinutes(90),
            DurationMinutes = 90,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();
    }

    private CreatePaymentIntentRequest ValidRequest() =>
        new(_appointmentId, _clientId, 200m, "eur");
}
