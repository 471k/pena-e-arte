using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class SubmitIntakeFormHandlerTests
{
    private readonly FakeDbContext  _db          = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant      = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid           _studioId    = Guid.NewGuid();

    public SubmitIntakeFormHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("artist");
    }

    private SubmitIntakeFormHandler CreateSut() => new(_db, _tenant, _currentUser);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsIntakeFormResponse()
    {
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{\"allergies\":\"none\"}", null);

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.StudioId.Should().Be(_studioId);
        result.ClientId.Should().Be(req.ClientId);
        result.FormData.Should().Be(req.FormData);
        result.AppointmentId.Should().BeNull();
        result.SubmittedAt.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithAppointmentId_PersistsAppointmentId()
    {
        Guid clientId      = await SeedClient();
        Guid appointmentId = await SeedAppointment(clientId);
        SubmitIntakeFormRequest req = new(clientId, appointmentId, "{\"health\":\"good\"}", null);

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.AppointmentId.Should().Be(appointmentId);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsFormToDb()
    {
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{\"allergies\":\"none\"}", null);

        await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        _db.IntakeForms.Should().ContainSingle(f => f.ClientId == req.ClientId && f.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_WithFileUrl_PersistsFileUrl()
    {
        const string fileUrl = "https://r2.example.com/form.pdf";
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{}", fileUrl);

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.FileUrl.Should().Be(fileUrl);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSubmittedAtToUtcNow()
    {
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{}", null);
        DateTime before = DateTime.UtcNow;

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.SubmittedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task Handle_AppointmentDoesNotExist_ThrowsNotFoundException()
    {
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "{}", null);

        Func<Task> act = () => CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AppointmentBelongsToDifferentClient_ThrowsNotFoundException()
    {
        Guid clientId      = await SeedClient();
        Guid otherClientId = await SeedClient();
        Guid appointmentId = await SeedAppointment(otherClientId);

        SubmitIntakeFormRequest req = new(clientId, appointmentId, "{}", null);

        Func<Task> act = () => CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ClientRole_IgnoresRequestClientId_UsesOwnIdentity()
    {
        Guid userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _currentUser.Role.Returns("client");
        Guid myClientId = await SeedClient(userId);

        // Request carries a different (spoofed) ClientId — handler must ignore it.
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{}", null);

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.ClientId.Should().Be(myClientId);
    }

    [Fact]
    public async Task Handle_ClientRole_NoClientRecord_ThrowsNotFoundException()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.Role.Returns("client");

        SubmitIntakeFormRequest req = new(Guid.NewGuid(), null, "{}", null);

        Func<Task> act = () => CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ClientRole_OwnAppointment_Succeeds()
    {
        Guid userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _currentUser.Role.Returns("client");
        Guid myClientId    = await SeedClient(userId);
        Guid appointmentId = await SeedAppointment(myClientId);

        SubmitIntakeFormRequest req = new(myClientId, appointmentId, "{}", null);

        IntakeFormResponse result = await CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

        result.AppointmentId.Should().Be(appointmentId);
        result.ClientId.Should().Be(myClientId);
    }

    [Fact]
    public async Task Handle_ClientRole_AppointmentBelongsToAnotherClient_ThrowsNotFoundException()
    {
        Guid userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _currentUser.Role.Returns("client");
        Guid myClientId    = await SeedClient(userId);
        Guid otherClientId = await SeedClient();
        Guid appointmentId = await SeedAppointment(otherClientId);

        SubmitIntakeFormRequest req = new(myClientId, appointmentId, "{}", null);

        Func<Task> act = () => CreateSut().Handle(new SubmitIntakeFormCommand(req), default);

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
