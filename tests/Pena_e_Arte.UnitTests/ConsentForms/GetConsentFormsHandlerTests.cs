using FluentAssertions;
using Pena_e_Arte.Application.ConsentForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class GetConsentFormsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetConsentFormsHandler CreateSut() => new(_db, FakeCurrentUser.Artist());

    private async Task SeedForm(Guid clientId, Guid appointmentId)
    {
        _db.ConsentForms.Add(new ConsentForm
        {
            StudioId      = _studioId,
            ClientId      = clientId,
            AppointmentId = appointmentId,
            SignatureData = "sig",
            SignedAt      = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllForms()
    {
        await SeedForm(Guid.NewGuid(), Guid.NewGuid());
        await SeedForm(Guid.NewGuid(), Guid.NewGuid());

        List<ConsentFormResponse> result = await CreateSut().Handle(new GetConsentFormsQuery(null, null), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ClientIdFilter_ReturnsOnlyMatchingForms()
    {
        Guid clientA = Guid.NewGuid();
        Guid clientB = Guid.NewGuid();
        await SeedForm(clientA, Guid.NewGuid());
        await SeedForm(clientB, Guid.NewGuid());

        List<ConsentFormResponse> result = await CreateSut().Handle(new GetConsentFormsQuery(clientA, null), default);

        result.Should().ContainSingle(f => f.ClientId == clientA);
    }

    [Fact]
    public async Task Handle_AppointmentIdFilter_ReturnsOnlyMatchingForms()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedForm(Guid.NewGuid(), appointmentId);
        await SeedForm(Guid.NewGuid(), Guid.NewGuid());

        List<ConsentFormResponse> result = await CreateSut().Handle(new GetConsentFormsQuery(null, appointmentId), default);

        result.Should().ContainSingle(f => f.AppointmentId == appointmentId);
    }

    [Fact]
    public async Task Handle_NoMatchingForms_ReturnsEmptyList()
    {
        List<ConsentFormResponse> result = await CreateSut().Handle(new GetConsentFormsQuery(Guid.NewGuid(), null), default);

        result.Should().BeEmpty();
    }
}
