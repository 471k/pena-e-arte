using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class UpsertIntakeFormTemplateHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public UpsertIntakeFormTemplateHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private UpsertIntakeFormTemplateHandler CreateSut() => new(_db, _tenant);

    private const string ValidSchema = """[{"label":"Allergies","type":"Text","required":true}]""";

    [Fact]
    public async Task Handle_NoExistingTemplate_CreatesOne()
    {
        UpsertIntakeFormTemplateRequest req = new(ValidSchema, true);

        IntakeFormTemplateResponse result = await CreateSut().Handle(new UpsertIntakeFormTemplateCommand(req), default);

        result.FieldSchemaJson.Should().Be(ValidSchema);
        result.IsActive.Should().BeTrue();
        result.StudioId.Should().Be(_studioId);
        _db.IntakeFormTemplates.Should().ContainSingle(t => t.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_ExistingTemplate_UpdatesInPlaceRatherThanCreatingSecond()
    {
        _db.IntakeFormTemplates.Add(new IntakeFormTemplate
        {
            StudioId = _studioId, FieldSchemaJson = """[{"label":"Old","type":"Text","required":false}]""", IsActive = false,
        });
        await _db.SaveChangesAsync();

        UpsertIntakeFormTemplateRequest req = new(ValidSchema, true);
        await CreateSut().Handle(new UpsertIntakeFormTemplateCommand(req), default);

        _db.IntakeFormTemplates.Should().ContainSingle(t => t.StudioId == _studioId);
        _db.IntakeFormTemplates.Single(t => t.StudioId == _studioId).FieldSchemaJson.Should().Be(ValidSchema);
    }

    [Fact]
    public async Task Handle_DoesNotAffectOtherStudiosTemplate()
    {
        Guid otherStudioId = Guid.NewGuid();
        _db.IntakeFormTemplates.Add(new IntakeFormTemplate
        {
            StudioId = otherStudioId, FieldSchemaJson = """[{"label":"Other","type":"Text","required":false}]""", IsActive = true,
        });
        await _db.SaveChangesAsync();

        UpsertIntakeFormTemplateRequest req = new(ValidSchema, true);
        await CreateSut().Handle(new UpsertIntakeFormTemplateCommand(req), default);

        _db.IntakeFormTemplates.Single(t => t.StudioId == otherStudioId).FieldSchemaJson
            .Should().Be("""[{"label":"Other","type":"Text","required":false}]""");
    }
}
