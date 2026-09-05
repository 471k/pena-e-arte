using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

/// <summary>
/// Returns the consent template of the given kind active for the caller's current studio, so a
/// consent page can render the exact text the client is about to agree to. Resolved server-side
/// (studio's own active template, else the platform default). Returns an empty template when
/// none is configured — the flow still works, the UI shows generic copy.
/// </summary>
public record GetActiveConsentTemplateQuery(ConsentTemplateKind Kind) : IRequest<ConsentTemplateResponse>;

public class GetActiveConsentTemplateHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetActiveConsentTemplateQuery, ConsentTemplateResponse>
{
    public async Task<ConsentTemplateResponse> Handle(
        GetActiveConsentTemplateQuery query, CancellationToken ct)
    {
        List<ConsentTemplate> candidates = await db.ConsentTemplates
            .Where(t => t.Kind == query.Kind
                        && t.IsActive
                        && (t.StudioId == tenant.StudioId || t.StudioId == null))
            .ToListAsync(ct);

        ConsentTemplate? template = ConsentTemplateResolver.ResolveActive(
            candidates, tenant.StudioId, query.Kind, DateTime.UtcNow);

        return new ConsentTemplateResponse(
            Id: template?.Id,
            Kind: query.Kind.ToString(),
            Version: template?.Version ?? string.Empty,
            BodyText: template?.BodyText ?? string.Empty);
    }
}
