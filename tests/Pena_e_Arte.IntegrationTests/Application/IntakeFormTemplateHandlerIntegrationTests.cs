using FluentAssertions;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class IntakeFormTemplateHandlerIntegrationTests(DatabaseFixture fixture)
{
    private const string ValidSchema = """[{"label":"Allergies","type":"Text","required":true}]""";

    [Fact]
    public async Task UpsertIntakeFormTemplate_FirstCall_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();

        IntakeFormTemplateResponse result = await RunUpsertHandler(tenantId, new(ValidSchema, true));

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        IntakeFormTemplate? stored = await verify.IntakeFormTemplates.FindAsync(result.Id);
        stored.Should().NotBeNull();
        stored!.FieldSchemaJson.Should().Be(ValidSchema);
    }

    [Fact]
    public async Task GetActiveIntakeFormTemplate_TenantIsolation_DoesNotLeakOtherStudiosTemplate()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await RunUpsertHandler(tenantB, new(ValidSchema, true));

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantA);
        GetActiveIntakeFormTemplateHandler handler = new(db);
        IntakeFormTemplateResponse? result = await handler.Handle(new GetActiveIntakeFormTemplateQuery(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveIntakeFormTemplate_OwnTenantActiveTemplate_ReturnsIt()
    {
        Guid tenantId = Guid.NewGuid();
        await RunUpsertHandler(tenantId, new(ValidSchema, true));

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetActiveIntakeFormTemplateHandler handler = new(db);
        IntakeFormTemplateResponse? result = await handler.Handle(new GetActiveIntakeFormTemplateQuery(), default);

        result.Should().NotBeNull();
        result!.FieldSchemaJson.Should().Be(ValidSchema);
    }

    private async Task<IntakeFormTemplateResponse> RunUpsertHandler(Guid tenantId, UpsertIntakeFormTemplateRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        UpsertIntakeFormTemplateHandler handler = new(db, tenant);
        return await handler.Handle(new UpsertIntakeFormTemplateCommand(req), default);
    }
}
