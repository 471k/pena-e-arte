namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicSocialLinkResponse(
    string Platform,     // SocialPlatform.ToString() — matches this codebase's existing
                         // HasConversion<string> convention of using the enum name directly.
    string Handle,
    bool IsVerified,
    string ProfileUrl);  // built server-side — the frontend never constructs platform URLs itself.
