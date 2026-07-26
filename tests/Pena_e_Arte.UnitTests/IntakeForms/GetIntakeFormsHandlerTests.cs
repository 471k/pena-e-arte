using FluentAssertions;
using Pena_e_Arte.Application.IntakeForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class GetIntakeFormsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetIntakeFormsHandler CreateSut() => new(_db, FakeCurrentUser.Artist());

    private async Task SeedForm(Guid clientId, Guid? appointmentId = null)
    {
        _db.IntakeForms.Add(new IntakeForm
        {
            StudioId = _studioId,
            ClientId = clientId,
            AppointmentId = appointmentId,
            FormData = "{}",
            SubmittedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllForms()
    {
        Guid clientA = Guid.NewGuid();
        Guid clientB = Guid.NewGuid();
        await SeedForm(clientA);
        await SeedForm(clientB);

        List<IntakeFormResponse> result = await CreateSut().Handle(new GetIntakeFormsQuery(null, null), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ClientIdFilter_ReturnsOnlyMatchingForms()
    {
        Guid clientA = Guid.NewGuid();
        Guid clientB = Guid.NewGuid();
        await SeedForm(clientA);
        await SeedForm(clientB);

        List<IntakeFormResponse> result = await CreateSut().Handle(new GetIntakeFormsQuery(clientA, null), default);

        result.Should().ContainSingle(f => f.ClientId == clientA);
    }

    [Fact]
    public async Task Handle_AppointmentIdFilter_ReturnsOnlyMatchingForms()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedForm(Guid.NewGuid(), appointmentId);
        await SeedForm(Guid.NewGuid(), null);

        List<IntakeFormResponse> result = await CreateSut().Handle(new GetIntakeFormsQuery(null, appointmentId), default);

        result.Should().ContainSingle(f => f.AppointmentId == appointmentId);
    }

    [Fact]
    public async Task Handle_NoMatchingForms_ReturnsEmptyList()
    {
        List<IntakeFormResponse> result = await CreateSut().Handle(new GetIntakeFormsQuery(Guid.NewGuid(), null), default);

        result.Should().BeEmpty();
    }
}
