using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public interface ISocialBioCheckerFactory
{
    /// <summary>Always resolves — all five platforms have a registered checker class;
    /// ISocialBioChecker.IsSupported (not the absence of a checker) is the gate for
    /// "this platform can't be verified this way".</summary>
    ISocialBioChecker GetChecker(SocialPlatform platform);
}
