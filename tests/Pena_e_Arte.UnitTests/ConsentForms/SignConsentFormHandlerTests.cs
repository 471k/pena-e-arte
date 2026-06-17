using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class SignConsentFormHandlerTests
{
    private readonly FakeDbContext  _db          = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant      = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _currentUser = Substitute.For<ICurrentUser>();
    private readonly ISender        _sender      = Substitute.For<ISender>();
    private readonly Guid           _studioId    = Guid.NewGuid();

    public SignConsentFormHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("artist");
    }

    private SignConsentFormHandler CreateSut() => new(_db, _tenant, _currentUser, _sender);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsConsentFormResponse()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "data:image/png;base64,abc123", null);

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.StudioId.Should().Be(_studioId);
        result.ClientId.Should().Be(clientId);
        result.AppointmentId.Should().Be(appointmentId);
        result.SignatureData.Should().Be(req.SignatureData);
        result.SignedAt.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsFormToDb()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "data:image/png;base64,abc123", null);

        await CreateSut().Handle(new SignConsentFormCommand(req), default);

        _db.ConsentForms.Should().ContainSingle(f =>
            f.AppointmentId == appointmentId && f.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_AppointmentAlreadySigned_ThrowsConsentFormAlreadySignedException()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        _db.ConsentForms.Add(new ConsentForm
        {
            StudioId      = _studioId,
            ClientId      = clientId,
            AppointmentId = appointmentId,
            SignatureData = "existing-sig",
            SignedAt      = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        SignConsentFormRequest req = new(clientId, appointmentId, "new-sig", null);

        Func<Task> act = () => CreateSut().Handle(new SignConsentFormCommand(req), default);

        await act.Should().ThrowAsync<ConsentFormAlreadySignedException>();
    }

    [Fact]
    public async Task Handle_DifferentAppointments_AllowsMultipleForms()
    {
        Guid clientId1      = await SeedClient();
        Guid appointmentId1 = await SeedAppointment(clientId1);
        Guid clientId2      = await SeedClient();
        Guid appointmentId2 = await SeedAppointment(clientId2);
        SignConsentFormRequest req1 = new(clientId1, appointmentId1, "sig1", null);
        SignConsentFormRequest req2 = new(clientId2, appointmentId2, "sig2", null);

        await CreateSut().Handle(new SignConsentFormCommand(req1), default);
        await CreateSut().Handle(new SignConsentFormCommand(req2), default);

        _db.ConsentForms.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithFileUrl_PersistsFileUrl()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        const string fileUrl = "https://r2.example.com/consent.pdf";
        SignConsentFormRequest req = new(clientId, appointmentId, "sig", fileUrl);

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.FileUrl.Should().Be(fileUrl);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSignedAtToUtcNow()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "sig", null);
        DateTime before = DateTime.UtcNow;

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.SignedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task Handle_AppointmentDoesNotExist_ThrowsNotFoundException()
    {
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "sig", null);

        Func<Task> act = () => CreateSut().Handle(new SignConsentFormCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ClientRole_OwnAppointment_DerivesClientIdFromAppointment()
    {
        Guid userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _currentUser.Role.Returns("client");
        Guid clientId      = await SeedClient(userId);
        Guid appointmentId = await SeedAppointment(clientId);

        // Request carries a different (spoofed) ClientId — handler must derive it from the appointment instead.
        SignConsentFormRequest req = new(Guid.NewGuid(), appointmentId, "sig", null);

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.ClientId.Should().Be(clientId);
    }

    [Fact]
    public async Task Handle_ClientRole_AppointmentBelongsToAnotherClient_ThrowsNotFoundException()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.Role.Returns("client");
        Guid otherClientId = await SeedClient();
        Guid appointmentId = await SeedAppointment(otherClientId);

        SignConsentFormRequest req = new(otherClientId, appointmentId, "sig", null);

        Func<Task> act = () => CreateSut().Handle(new SignConsentFormCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedClient(Guid? userId = null)
    {
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = userId,
            FirstName = "Test",
            LastName  = "Client",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid clientId)
    {
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = clientId,
            Date            = DateTime.UtcNow.AddDays(5),
            EndDate         = DateTime.UtcNow.AddDays(5).AddMinutes(60),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
            DepositAmount   = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }
}
