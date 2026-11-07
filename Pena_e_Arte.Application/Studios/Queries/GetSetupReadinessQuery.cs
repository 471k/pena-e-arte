using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Studios.Queries;

/// <summary>
/// GET /studios/me/setup-readiness (OwnerOnly): where each setup area stands (E05a, O02). Replaces the
/// checklist's old "a row exists" test, which called every new studio's hours done because registration
/// seeds them. The derivation is <see cref="SetupReadiness"/>; this only reads the facts, in one query.
/// </summary>
public record GetSetupReadinessQuery : IRequest<SetupReadinessResponse>;

public class GetSetupReadinessValidator : AbstractValidator<GetSetupReadinessQuery>
{
    // No input: the studio comes from the caller's tenant.
}

public class GetSetupReadinessHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetSetupReadinessQuery, SetupReadinessResponse>
{
    private sealed record Facts(bool AnyArtist, bool AnyDepositRule, bool AnyHours, DateTime? HoursReviewedAt);

    public async Task<SetupReadinessResponse> Handle(GetSetupReadinessQuery query, CancellationToken ct)
    {
        // One statement: each fact is an EXISTS subquery on the studio row, tenant filters applied.
        Facts facts = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => new Facts(
                db.Artists.Any(),
                db.DepositRules.Any(),
                db.StudioHours.Any(),
                s.HoursReviewedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        return new SetupReadinessResponse(
        [
            new SetupAreaResponse("Artists", SetupReadiness.OwnerCreated(facts.AnyArtist).ToString(), null),
            new SetupAreaResponse("DepositRules", SetupReadiness.OwnerCreated(facts.AnyDepositRule).ToString(), null),
            new SetupAreaResponse(
                "Hours",
                SetupReadiness.Hours(facts.AnyHours, facts.HoursReviewedAt).ToString(),
                facts.HoursReviewedAt),
        ]);
    }
}
