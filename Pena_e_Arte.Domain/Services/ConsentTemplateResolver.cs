using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// Resolves the active <see cref="ConsentTemplate"/> for a studio and kind at a point in
/// time: prefer the studio's own active template (most recent EffectiveFrom that is not in
/// the future), falling back to the platform-default template (StudioId == null). Pure and
/// side-effect-free so it is unit-testable without a database — the handler supplies the
/// candidate rows (already narrowed to the studio + platform defaults) and "now".
/// </summary>
public static class ConsentTemplateResolver
{
    public static ConsentTemplate? ResolveActive(
        IEnumerable<ConsentTemplate> candidates,
        Guid studioId,
        ConsentTemplateKind kind,
        DateTime nowUtc)
    {
        List<ConsentTemplate> eligible = candidates
            .Where(t => t.Kind == kind && t.IsActive && t.EffectiveFrom <= nowUtc)
            .ToList();

        ConsentTemplate? studioOwned = eligible
            .Where(t => t.StudioId == studioId)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefault();

        if (studioOwned is not null) return studioOwned;

        return eligible
            .Where(t => t.StudioId is null)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefault();
    }
}
