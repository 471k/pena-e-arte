using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Social;

public sealed class SocialBioCheckerFactory(IEnumerable<ISocialBioChecker> checkers) : ISocialBioCheckerFactory
{
    private readonly Dictionary<SocialPlatform, ISocialBioChecker> _byPlatform =
        checkers.ToDictionary(c => c.Platform);

    public ISocialBioChecker GetChecker(SocialPlatform platform) =>
        _byPlatform.TryGetValue(platform, out ISocialBioChecker? checker)
            ? checker
            : throw new InvalidOperationException($"No ISocialBioChecker registered for {platform}.");
}
