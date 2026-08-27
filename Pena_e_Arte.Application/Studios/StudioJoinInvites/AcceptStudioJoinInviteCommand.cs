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
        // same ownership-check convention used elsewhere in this codebase.
        StudioJoinInvite? invite = await db.StudioJoinInvites.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.Id == command.InviteId
                && i.InvitedEmail.ToLower() == currentUser.Email.ToLower(), ct);

        if (invite is null || invite.Status != StudioJoinInviteStatus.Pending || invite.ExpiresAt <= DateTime.UtcNow)
            throw new NotFoundException(nameof(StudioJoinInvite), command.InviteId);

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

        // Mark the invite Accepted in the same write as the domain changes above — one atomic
        // DB commit (Identity's role/claim/token propagation below is a separate store and
        // cannot join this transaction; this mirrors SwitchStudioHandler's existing "DB state is
        // the source of truth, Identity propagation follows" ordering).
        invite.Status = StudioJoinInviteStatus.Accepted;
        invite.RespondedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await planLimits.InvalidateUsageCacheAsync(QuotaType.Artists, ct);

        // Role/tenant swap: remove owner role + old studio's tenant_id claim, add artist role +
        // new studio's tenant_id claim, then issue fresh tokens scoped to the new studio.
        await identity.SwapRoleAsync(currentUser.UserId, "owner", "artist", ct);
        await identity.RemoveTenantClaimAsync(currentUser.UserId, callerSoloStudio.Id, ct);
        await identity.EnsureTenantClaimAsync(currentUser.UserId, invite.StudioId, ct);

        (bool success, string? accessToken, string? refreshToken, string? error) =
            await identity.IssueTokensForTenantAsync(currentUser.UserId, invite.StudioId, ct);

        if (!success) throw new BusinessRuleViolationException(error ?? "Could not switch studio.");

        logger.LogInformation(
            "User {@UserId} accepted join invite {@InviteId}: closed studio {@OldStudioId}, joined {@NewStudioId} as artist",
            currentUser.UserId, invite.Id, callerSoloStudio.Id, invite.StudioId);

        return new AuthResponse(accessToken!, refreshToken!);
    }
}
