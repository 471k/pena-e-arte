using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.Waitlists;

/// <summary>
/// Data Protection over the app's shared key ring (DataProtectionExtensions: one ring for every replica,
/// persisted in the database, guarded at startup), under a purpose of its own. No new secret: unlike the
/// HMAC signers (MarketingOptOutSigner, SocialOAuthStateSigner) this needs nothing provisioned per cluster,
/// and Data Protection rotates its keys while still reading tokens made with older ones.
///
/// Payload: "{entryId:N}|{studioId:N}|{NotifiedAt ticks}", protected, then base64url. Deliberately not a
/// time-limited protector: an expired time-limited payload fails to unprotect, which would make an expired
/// link look exactly like a forged one; the window is checked against the row instead.
/// </summary>
public sealed class WaitlistClaimTokens(IDataProtectionProvider provider) : IWaitlistClaimTokens
{
    /// <summary>Changing this string invalidates every link already sent. Bump the version only on purpose.</summary>
    public const string Purpose = "Waitlist.Claim.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Mint(Waitlist entry)
    {
        DateTime notifiedAt = entry.NotifiedAt
            ?? throw new InvalidOperationException("A claim token can only be minted for a notified waitlist entry.");

        string payload = string.Create(CultureInfo.InvariantCulture,
            $"{entry.Id:N}|{entry.StudioId:N}|{notifiedAt.Ticks}");
        byte[] protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(payload));
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    public bool TryRead(string? token, out WaitlistClaimRef claim)
    {
        claim = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;

        string payload;
        try
        {
            byte[] protectedBytes = WebEncoders.Base64UrlDecode(token);
            payload = Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }

        string[] parts = payload.Split('|');
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "N", out Guid entryId)
            || !Guid.TryParseExact(parts[1], "N", out Guid studioId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
        {
            return false;
        }

        claim = new WaitlistClaimRef(entryId, studioId, ticks);
        return true;
    }
}
