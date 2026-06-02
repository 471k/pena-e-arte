using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class SubmitIntakeFormHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public SubmitIntakeFormHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private SubmitIntakeFormHandler CreateSut() => new(_db, _tenant);

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
        Guid appointmentId = Guid.NewGuid();
        SubmitIntakeFormRequest req = new(Guid.NewGuid(), appointmentId, "{\"health\":\"good\"}", null);

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
}
