using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record CreateArtistCommand(CreateArtistRequest Request)
    : IRequest<ArtistResponse>, IQuotaCheckedCommand
{
    public QuotaType QuotaType => QuotaType.Artists;
}

public class CreateArtistHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IIdentityService identity,
    IJobScheduler scheduler,
    IPlanLimitService planLimits)
    : IRequestHandler<CreateArtistCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(CreateArtistCommand command, CancellationToken ct)
    {
        CreateArtistRequest req = command.Request;

        bool exists = await db.Artists.AnyAsync(a => a.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"An artist with email '{req.Email}' already exists in this studio.");

        string slug = await ArtistSlugHelper.GenerateUniqueSlugAsync(db, req.FirstName, req.LastName, ct);

        // Create the Identity login account for the artist
        string tempPassword = $"Tmp!{Guid.NewGuid():N}";
        (bool created, Guid userId, string[] errors) =
            await identity.CreateUserAsync(req.Email, tempPassword, "artist", tenant.StudioId, req.FirstName);

        if (!created)
        {
            bool emailTaken = errors.Any(e => e.Contains("already taken", StringComparison.OrdinalIgnoreCase));
            if (!emailTaken)
                throw new BusinessRuleViolationException($"Failed to create artist account: {string.Join(", ", errors)}");

            Guid? existingId = await identity.GetUserIdByEmailAsync(req.Email, ct);
            if (existingId is null)
                throw new BusinessRuleViolationException($"The email '{req.Email}' is already registered to another account. Each artist must have a unique email address.");

            // The only safe reason for this email to already exist in Identity is a genuinely
            // orphaned artist account: a previous CreateArtistCommand call for THIS studio got
            // as far as creating the Identity user (already holding the "artist" role and this
            // studio's tenant_id claim — see CreateUserAsync) but never made it to persisting
            // the Artist row below, e.g. a crash between identity.CreateUserAsync and
            // SaveChangesAsync. That case is safe to recover by reusing the existing user's ID.
            //
            // Any other case — the email belongs to an owner, client, or admin account, or to
            // an artist who already belongs to a DIFFERENT studio — must be rejected outright.
            // Silently reusing that account's ID here would grant it artist access to this
            // tenant's data without the account holder's consent or knowledge, violating tenant
            // isolation (CLAUDE.md Non-Negotiable Rule #1). Only the "client" role supports
            // belonging to more than one studio (see GenerateJwt's tenant-claim comment in
            // IdentityService and architecture.md's "Multi-Studio Client View" entry) — artist
            // and owner accounts are single-studio by design, so any cross-studio match here is
            // always wrong.
            IReadOnlyList<string> existingRoles = await identity.GetUserRolesAsync(existingId.Value, ct);
            IReadOnlyList<Guid> existingTenantIds = await identity.GetTenantIdsAsync(existingId.Value, ct);
            bool isOrphanedArtistForThisStudio =
                existingRoles.Contains("artist") && existingTenantIds.Contains(tenant.StudioId);

            if (!isOrphanedArtistForThisStudio)
                throw new BusinessRuleViolationException(
                    $"The email '{req.Email}' already belongs to an existing account and cannot be invited as an artist here.");

            userId = existingId.Value;
        }

        Artist artist = new()
        {
            StudioId = tenant.StudioId,
            UserId = userId,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = req.Email,
            Specializations = req.Specializations,
            HourlyRate = req.HourlyRate
        };
        artist.SetSlug(slug);

        db.Artists.Add(artist);
        await db.SaveChangesAsync(ct);

        // Write-through cache invalidation — the next EnsureWithinLimitAsync call for
        // this studio reflects this new artist immediately instead of up to 30s later.
        await planLimits.InvalidateUsageCacheAsync(QuotaType.Artists, ct);

        // Fire-and-forget via Hangfire so the HTTP response returns immediately
        scheduler.EnqueueArtistInvite(req.Email, req.FirstName, tenant.StudioId);

        return Map(artist);
    }

    internal static ArtistResponse Map(Artist a) =>
        new(a.Id, a.StudioId, a.UserId, a.FirstName, a.LastName, a.Email, a.Specializations, a.HourlyRate,
            a.IsActive, a.AvatarUrl,
            a.Portfolio.OrderByDescending(p => p.CreatedAt)
                .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl, p.Style, p.Category))
                .ToList(),
            a.Slug, a.CreatedAt, a.UpdatedAt);
}
