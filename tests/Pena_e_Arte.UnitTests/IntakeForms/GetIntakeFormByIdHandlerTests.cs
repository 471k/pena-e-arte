using FluentAssertions;
using Pena_e_Arte.Application.IntakeForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class GetIntakeFormByIdHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetIntakeFormByIdHandler CreateSut() => new(_db, FakeCurrentUser.Artist());

    private async Task<Guid> SeedForm()
    {
        IntakeForm form = new()
        {
            StudioId    = _studioId,
            ClientId    = Guid.NewGuid(),
            FormData    = "{\"allergies\":\"none\"}",
            SubmittedAt = DateTime.UtcNow
        };
        _db.IntakeForms.Add(form);
        await _db.SaveChangesAsync();
        return form.Id;
    }

    [Fact]
    public async Task Handle_ExistingId_ReturnsIntakeFormResponse()
    {
        Guid id = await SeedForm();

        IntakeFormResponse result = await CreateSut().Handle(new GetIntakeFormByIdQuery(id), default);

        result.Id.Should().Be(id);
        result.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetIntakeFormByIdQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
