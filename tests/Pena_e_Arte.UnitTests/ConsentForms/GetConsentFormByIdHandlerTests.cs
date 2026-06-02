using FluentAssertions;
using Pena_e_Arte.Application.ConsentForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class GetConsentFormByIdHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetConsentFormByIdHandler CreateSut() => new(_db);

    private async Task<Guid> SeedForm()
    {
        ConsentForm form = new()
        {
            StudioId      = _studioId,
            ClientId      = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            SignatureData = "sig",
            SignedAt      = DateTime.UtcNow
        };
        _db.ConsentForms.Add(form);
        await _db.SaveChangesAsync();
        return form.Id;
    }

    [Fact]
    public async Task Handle_ExistingId_ReturnsConsentFormResponse()
    {
        Guid id = await SeedForm();

        ConsentFormResponse result = await CreateSut().Handle(new GetConsentFormByIdQuery(id), default);

        result.Id.Should().Be(id);
        result.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetConsentFormByIdQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
