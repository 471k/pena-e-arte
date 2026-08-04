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
/// Returns the appointment-consent template active for the caller's current studio, so the
/// sign-consent page can render the exact text the client is about to agree to. Resolved
/// server-side (studio's own active template, else the platform default). Returns an empty
/// template when none is configured — signing still works, the UI shows generic copy.
/// </summary>
public record GetActiveConsentTemplateQuery() : IRequest<ConsentTemplateResponse>;

public class GetActiveConsentTemplateHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetActiveConsentTemplateQuery, ConsentTemplateResponse>
{
    public async Task<ConsentTemplateResponse> Handle(
        GetActiveConsentTemplateQuery query, CancellationToken ct)
    {
        List<ConsentTemplate> candidates = await db.ConsentTemplates
            .Where(t => t.Kind == ConsentTemplateKind.AppointmentConsent
                        && t.IsActive
                        && (t.StudioId == tenant.StudioId || t.StudioId == null))
            .ToListAsync(ct);

        ConsentTemplate? template = ConsentTemplateResolver.ResolveActive(
            candidates, tenant.StudioId, ConsentTemplateKind.AppointmentConsent, DateTime.UtcNow);

        return new ConsentTemplateResponse(
            Id: template?.Id,
            Kind: ConsentTemplateKind.AppointmentConsent.ToString(),
            Version: template?.Version ?? string.Empty,
            BodyText: template?.BodyText ?? string.Empty);
    }
}
