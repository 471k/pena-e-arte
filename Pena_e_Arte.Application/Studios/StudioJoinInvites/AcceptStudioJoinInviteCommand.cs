using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Artists;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.StudioJoinInvites;

public record AcceptStudioJoinInviteCommand(Guid InviteId) : IRequest<AuthResponse>;

public class AcceptStudioJoinInviteHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    IPlanLimitService planLimits,
    ILogger<AcceptStudioJoinInviteHandler> logger)
    : IRequestHandler<AcceptStudioJoinInviteCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(AcceptStudioJoinInviteCommand command, CancellationToken ct)
    {
        if (currentUser.Email is null)
            throw new NotFoundException(nameof(StudioJoinInvite), command.InviteId);

        // IgnoreQueryFilters + explicit checks: invites are cross-tenant by nature (see
        // AppDbContext). 404, not 403, for "not addressed to me" / already-responded / expired —
        // same ownership-check convention used elsewhere in this codebase. Plain == (not
        // .ToLower()) — MySQL's default collation is already case-insensitive, and .ToLower() on
        // both sides would prevent the invited-email index from being used for no benefit.
        StudioJoinInvite? invite = await db.StudioJoinInvites.IgnoreQueryFilters()
            .Include(i => i.Studio)
            .FirstOrDefaultAsync(i =>
                i.Id == command.InviteId
                && i.InvitedEmail == currentUser.Email, ct);

        if (invite is null || invite.Status != StudioJoinInviteStatus.Pending || invite.ExpiresAt <= DateTime.UtcNow)
            throw new NotFoundException(nameof(StudioJoinInvite), command.InviteId);

        // The inviting studio may have been suspended/deactivated between invite creation and
        // acceptance — GetMyStudioJoinInvitesQuery filters this case out of the list view, but
        // it must be re-checked here too: without it, accepting would close the caller's own
        // working solo studio in exchange for a studio that will reject every request.
        if (!invite.Studio.IsActive)
            throw new BusinessRuleViolationException(
                "This studio is no longer active and can't be joined.");

        // The caller must currently be the owner of exactly one IsSolo studio — their own solo
        // studio. If they've already dissolved it, or this isn't actually a solo artist account
        // any more, there is nothing to convert.
        Studio? callerSoloStudio = await db.Studios.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.OwnerEmail == currentUser.Email && s.IsSolo && s.IsActive, ct);

        if (callerSoloStudio is null)
            throw new BusinessRuleViolationException(
                "Your account is not currently the owner of an independent solo studio.");

        // Explicitly scoped to invite.StudioId, not the caller's current tenant — the caller's
        // JWT still carries their old solo studio as the active tenant at this point, so the
        // ICurrentTenant-scoped IQuotaCheckedCommand pipeline behavior cannot be used here.
        await planLimits.EnsureWithinLimitAsync(invite.StudioId, QuotaType.Artists, ct);

        string slug = await ArtistSlugHelper.GenerateUniqueSlugAsync(db, invite.FirstName, invite.LastName, ct);

        // Identity role/tenant swap runs BEFORE the DB write below, not after. Every one of
        // these calls is idempotent (SwapRoleAsync/EnsureTenantClaimAsync/RemoveTenantClaimAsync
        // no-op if already applied; IssueTokensForTenantAsync just reissues), so if the DB write
        // that follows fails, the invite is still Pending and a retry safely re-applies whatever
        // didn't already land, then completes the DB write. The reverse ordering (DB write, then
        // Identity) has no such recovery: once the invite is marked Accepted, a retry after an
        // Identity failure is permanently blocked (see the earlier ordering's post-mortem in the
        // Decisions Log) — accept without a real recovery path is worse than an extra idempotent
        // Identity call on the happy path.
        await identity.SwapRoleAsync(currentUser.UserId, "owner", "artist", ct);
        await identity.RemoveTenantClaimAsync(currentUser.UserId, callerSoloStudio.Id, ct);
        await identity.EnsureTenantClaimAsync(currentUser.UserId, invite.StudioId, ct);

        (bool success, string? accessToken, string? refreshToken, string? error) =
            await identity.IssueTokensForTenantAsync(currentUser.UserId, invite.StudioId, ct);

        if (!success) throw new BusinessRuleViolationException(error ?? "Could not switch studio.");

        Artist artist = new()
        {
            StudioId = invite.StudioId,
            UserId = currentUser.UserId,
            FirstName = invite.FirstName,
            LastName = invite.LastName,
            Email = currentUser.Email,
            Specializations = invite.Specializations,
            HourlyRate = invite.HourlyRate,
        };
        artist.SetSlug(slug);
        db.Artists.Add(artist);

        // Soft-close the old solo studio — retain all its data (appointments, clients,
        // portfolio, payments), never delete or cross-tenant-copy any of it (see the
        // solo-independent-artist overnight prompt's "Open product question").
        callerSoloStudio.IsActive = false;
        callerSoloStudio.ClosedAt = DateTime.UtcNow;

        invite.Status = StudioJoinInviteStatus.Accepted;
        invite.RespondedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // Explicit-studioId overload: the caller's ambient tenant is still the now-closed old
        // studio at this point (RemoveTenantClaimAsync above only updates the Identity claim
        // store, not this request's already-resolved ICurrentTenant) — the ICurrentTenant-scoped
        // overload would invalidate the wrong studio's cache entry.
        await planLimits.InvalidateUsageCacheAsync(invite.StudioId, QuotaType.Artists, ct);

        logger.LogInformation(
            "User {@UserId} accepted join invite {@InviteId}: closed studio {@OldStudioId}, joined {@NewStudioId} as artist",
            currentUser.UserId, invite.Id, callerSoloStudio.Id, invite.StudioId);

        return new AuthResponse(accessToken!, refreshToken!);
    }
}
