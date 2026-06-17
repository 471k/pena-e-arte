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
    private readonly FakeDbContext         _db          = FakeDbContext.Create();
    private readonly ICurrentTenant        _tenant      = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser          _currentUser = Substitute.For<ICurrentUser>();
    private readonly IConsentFormPdfService _pdf        = Substitute.For<IConsentFormPdfService>();
    private readonly IR2Service            _r2          = Substitute.For<IR2Service>();
    private readonly ISender               _sender      = Substitute.For<ISender>();
    private readonly Guid                  _studioId    = Guid.NewGuid();

    public SignConsentFormHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("artist");
        _pdf.Generate(Arg.Any<ConsentFormPdfData>()).Returns([0x25, 0x50, 0x44, 0x46]); // "%PDF"
        _r2.GetPublicUrl(Arg.Any<string>()).Returns("https://r2.example.com/consent/test.pdf");
    }

    private SignConsentFormHandler CreateSut() =>
        new(_db, _tenant, _currentUser, _pdf, _r2, _sender);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsConsentFormResponse()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "data:image/png;base64,abc123");

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
        SignConsentFormRequest req = new(clientId, appointmentId, "data:image/png;base64,abc123");

        await CreateSut().Handle(new SignConsentFormCommand(req), default);

        _db.ConsentForms.Should().ContainSingle(f =>
            f.AppointmentId == appointmentId && f.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesPdfAndPersistsFileUrl()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "Jane Doe");

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        _pdf.Received(1).Generate(Arg.Any<ConsentFormPdfData>());
        await _r2.Received(1).UploadAsync(
            Arg.Is<string>(k => k.StartsWith("consent/") && k.EndsWith(".pdf")),
            Arg.Any<byte[]>(),
            "application/pdf",
            Arg.Any<CancellationToken>());
        result.FileUrl.Should().Be("https://r2.example.com/consent/test.pdf");
    }

    [Fact]
    public async Task Handle_PdfServiceThrows_SigningStillSucceeds()
    {
        _pdf.When(p => p.Generate(Arg.Any<ConsentFormPdfData>()))
            .Do(_ => throw new InvalidOperationException("pdf error"));
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SignConsentFormRequest req = new(clientId, appointmentId, "sig");

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.Id.Should().NotBeEmpty();
        result.FileUrl.Should().BeNull();
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

        SignConsentFormRequest req = new(clientId, appointmentId, "new-sig");

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

        await CreateSut().Handle(new SignConsentFormCommand(new(clientId1, appointmentId1, "sig1")), default);
        await CreateSut().Handle(new SignConsentFormCommand(new(clientId2, appointmentId2, "sig2")), default);

        _db.ConsentForms.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSignedAtToUtcNow()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        DateTime before    = DateTime.UtcNow;

        ConsentFormResponse result = await CreateSut().Handle(
            new SignConsentFormCommand(new(clientId, appointmentId, "sig")), default);

        result.SignedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task Handle_AppointmentDoesNotExist_ThrowsNotFoundException()
    {
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "sig");

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
        SignConsentFormRequest req = new(Guid.NewGuid(), appointmentId, "sig");

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

        SignConsentFormRequest req = new(otherClientId, appointmentId, "sig");

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
