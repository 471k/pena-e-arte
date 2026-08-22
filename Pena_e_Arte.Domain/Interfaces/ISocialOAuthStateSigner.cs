using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Signs the OAuth `state` parameter for the generic social-verification connect flows
/// (studio Instagram, TikTok, Facebook, X, YouTube) so the anonymous callback can trust
/// it came from a connect-url this API generated. Separate from IInstagramStateSigner,
/// which stays artist-Instagram-only and is unmodified by this feature — that signer's
/// payload is a bare artistId; this one carries a (subjectType, subjectId, platform)
/// triple, since a single subject can now be linked across five platforms and both
/// artists and studios can be the subject.
/// </summary>
public interface ISocialOAuthStateSigner
{
    string Sign(SocialLinkSubjectType subjectType, Guid subjectId, SocialPlatform platform);

    bool TryValidate(
        string state,
        out SocialLinkSubjectType subjectType,
        out Guid subjectId,
        out SocialPlatform platform);
}
