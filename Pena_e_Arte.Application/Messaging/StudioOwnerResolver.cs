using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging;

/// <summary>
/// Who "the studio owner" is, for anything that messages them (DEC06). One rule, used both by
/// ConversationEligibility (so CreateConversation accepts the owner) and by the client appointment
/// page (so it only offers a compose box the server will accept):
/// <list type="number">
/// <item>The studio's declared owner: the account whose email is Studio.OwnerEmail. Registration binds
/// the owner account to that address, so this is the normal answer and it is unchanged from before.</item>
/// <item>Otherwise (the owner changed their login email, so OwnerEmail no longer matches an account):
/// an "owner" account holding the studio's tenant claim and not locked out, the lowest user id winning
/// when there are several (IIdentityService.GetStudioOwnerUserIdAsync).</item>
/// <item>Otherwise none. Callers then show the studio's contact details and never a compose box; a
/// recipient is never guessed.</item>
/// </list>
/// </summary>
internal static class StudioOwnerResolver
{
    public const string FallbackDisplayName = "Studio Owner";

    public static async Task<(Guid? UserId, string DisplayName)> ResolveAsync(
        IAppDbContext db, IIdentityService identity, Guid studioId, CancellationToken ct)
    {
        Domain.Entities.Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == studioId, ct);
        if (studio is null) return (null, FallbackDisplayName);

        Guid? declared = string.IsNullOrWhiteSpace(studio.OwnerEmail)
            ? null
            : await identity.GetUserIdByEmailAsync(studio.OwnerEmail, ct);
        if (declared is not null)
        {
            string? declaredName = await identity.GetUserDisplayNameAsync(studio.OwnerEmail, ct);
            return (declared, declaredName ?? FallbackDisplayName);
        }

        Guid? fallback = await identity.GetStudioOwnerUserIdAsync(studioId, ct);
        if (fallback is null) return (null, FallbackDisplayName);

        string? email = await identity.GetUserEmailAsync(fallback.Value, ct);
        string? name = email is null ? null : await identity.GetUserDisplayNameAsync(email, ct);
        return (fallback, name ?? FallbackDisplayName);
    }
}
