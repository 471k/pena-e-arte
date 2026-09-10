using FluentAssertions;
using Pena_e_Arte.Application.IntakeForms.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class GetActiveIntakeFormTemplateHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetActiveIntakeFormTemplateHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoTemplateConfigured_ReturnsNull()
    {
        IntakeFormTemplateResponse? result = await CreateSut().Handle(new GetActiveIntakeFormTemplateQuery(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_InactiveTemplateOnly_ReturnsNull()
    {
        _db.IntakeFormTemplates.Add(new IntakeFormTemplate
        {
            StudioId = _studioId,
            FieldSchemaJson = """[{"label":"X","type":"Text","required":false}]""",
            IsActive = false,
        });
        await _db.SaveChangesAsync();

        IntakeFormTemplateResponse? result = await CreateSut().Handle(new GetActiveIntakeFormTemplateQuery(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ActiveTemplate_ReturnsIt()
    {
        _db.IntakeFormTemplates.Add(new IntakeFormTemplate
        {
            StudioId = _studioId,
            FieldSchemaJson = """[{"label":"X","type":"Text","required":false}]""",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        IntakeFormTemplateResponse? result = await CreateSut().Handle(new GetActiveIntakeFormTemplateQuery(), default);

        result.Should().NotBeNull();
        result!.IsActive.Should().BeTrue();
    }
}
