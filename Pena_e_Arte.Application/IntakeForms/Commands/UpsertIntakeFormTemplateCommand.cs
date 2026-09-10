using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.IntakeForms.Commands;

public record UpsertIntakeFormTemplateCommand(UpsertIntakeFormTemplateRequest Request) : IRequest<IntakeFormTemplateResponse>;

public class UpsertIntakeFormTemplateHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpsertIntakeFormTemplateCommand, IntakeFormTemplateResponse>
{
    public async Task<IntakeFormTemplateResponse> Handle(UpsertIntakeFormTemplateCommand command, CancellationToken ct)
    {
        UpsertIntakeFormTemplateRequest req = command.Request;

        // One template per studio — editing in place (no versioning, unlike ConsentTemplate;
        // flagged as an accepted gap in the P1 backlog audit).
        IntakeFormTemplate? template = await db.IntakeFormTemplates
            .FirstOrDefaultAsync(t => t.StudioId == tenant.StudioId, ct);

        if (template is null)
        {
            template = new IntakeFormTemplate { StudioId = tenant.StudioId };
            db.IntakeFormTemplates.Add(template);
        }

        template.FieldSchemaJson = req.FieldSchemaJson;
        template.IsActive = req.IsActive;
        template.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return Map(template);
    }

    internal static IntakeFormTemplateResponse Map(IntakeFormTemplate t) => new(
        t.Id, t.StudioId, t.FieldSchemaJson, t.IsActive, t.CreatedAt, t.UpdatedAt);
}
