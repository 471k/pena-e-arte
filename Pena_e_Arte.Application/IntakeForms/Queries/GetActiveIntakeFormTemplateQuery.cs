using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

/// <summary>
/// Returns the caller's studio's active intake-form template, or null when none is configured —
/// unlike ConsentTemplate, IntakeFormTemplate is a plain TenantEntity with no platform-default
/// fallback, so resolution here is purely "does this studio have one active", nothing else.
/// SubmitIntakeFormPage renders today's single-textarea fallback when this returns null.
/// </summary>
public record GetActiveIntakeFormTemplateQuery : IRequest<IntakeFormTemplateResponse?>;

public class GetActiveIntakeFormTemplateHandler(IAppDbContext db)
    : IRequestHandler<GetActiveIntakeFormTemplateQuery, IntakeFormTemplateResponse?>
{
    public async Task<IntakeFormTemplateResponse?> Handle(
        GetActiveIntakeFormTemplateQuery query, CancellationToken ct)
    {
        IntakeFormTemplate? template = await db.IntakeFormTemplates
            .FirstOrDefaultAsync(t => t.IsActive, ct);

        return template is null ? null : UpsertIntakeFormTemplateHandler.Map(template);
    }
}
