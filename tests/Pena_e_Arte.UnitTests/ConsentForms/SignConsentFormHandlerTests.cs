using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class SignConsentFormHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public SignConsentFormHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private SignConsentFormHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsConsentFormResponse()
    {
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "data:image/png;base64,abc123", null);

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.StudioId.Should().Be(_studioId);
        result.ClientId.Should().Be(req.ClientId);
        result.AppointmentId.Should().Be(req.AppointmentId);
        result.SignatureData.Should().Be(req.SignatureData);
        result.SignedAt.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsFormToDb()
    {
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "data:image/png;base64,abc123", null);

        await CreateSut().Handle(new SignConsentFormCommand(req), default);

        _db.ConsentForms.Should().ContainSingle(f =>
            f.AppointmentId == req.AppointmentId && f.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_AppointmentAlreadySigned_ThrowsConsentFormAlreadySignedException()
    {
        Guid appointmentId = Guid.NewGuid();
        _db.ConsentForms.Add(new ConsentForm
        {
            StudioId      = _studioId,
            ClientId      = Guid.NewGuid(),
            AppointmentId = appointmentId,
            SignatureData = "existing-sig",
            SignedAt      = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        SignConsentFormRequest req = new(Guid.NewGuid(), appointmentId, "new-sig", null);

        Func<Task> act = () => CreateSut().Handle(new SignConsentFormCommand(req), default);

        await act.Should().ThrowAsync<ConsentFormAlreadySignedException>();
    }

    [Fact]
    public async Task Handle_DifferentAppointments_AllowsMultipleForms()
    {
        SignConsentFormRequest req1 = new(Guid.NewGuid(), Guid.NewGuid(), "sig1", null);
        SignConsentFormRequest req2 = new(Guid.NewGuid(), Guid.NewGuid(), "sig2", null);

        await CreateSut().Handle(new SignConsentFormCommand(req1), default);
        await CreateSut().Handle(new SignConsentFormCommand(req2), default);

        _db.ConsentForms.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithFileUrl_PersistsFileUrl()
    {
        const string fileUrl = "https://r2.example.com/consent.pdf";
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "sig", fileUrl);

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.FileUrl.Should().Be(fileUrl);
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsSignedAtToUtcNow()
    {
        SignConsentFormRequest req = new(Guid.NewGuid(), Guid.NewGuid(), "sig", null);
        DateTime before = DateTime.UtcNow;

        ConsentFormResponse result = await CreateSut().Handle(new SignConsentFormCommand(req), default);

        result.SignedAt.Should().BeOnOrAfter(before);
    }
}
