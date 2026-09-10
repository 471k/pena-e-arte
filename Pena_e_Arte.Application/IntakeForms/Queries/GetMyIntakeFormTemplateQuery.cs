using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.IntakeForms.Queries;

/// <summary>Owner-facing: the caller's studio's intake-form template regardless of IsActive, so
/// the builder page can load an existing (possibly deactivated) configuration to re-edit. Distinct
/// from GetActiveIntakeFormTemplateQuery, which is the client-submission-facing active-only read.</summary>
public record GetMyIntakeFormTemplateQuery : IRequest<IntakeFormTemplateResponse?>;

public class GetMyIntakeFormTemplateHandler(IAppDbContext db)
    : IRequestHandler<GetMyIntakeFormTemplateQuery, IntakeFormTemplateResponse?>
{
    public async Task<IntakeFormTemplateResponse?> Handle(
        GetMyIntakeFormTemplateQuery query, CancellationToken ct)
    {
        IntakeFormTemplate? template = await db.IntakeFormTemplates.FirstOrDefaultAsync(ct);

        return template is null ? null : UpsertIntakeFormTemplateHandler.Map(template);
    }
}
