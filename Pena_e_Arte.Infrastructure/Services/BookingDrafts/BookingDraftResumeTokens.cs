using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.BookingDrafts;

/// <summary>
/// C05, a copy of WaitlistClaimTokens' pattern. Data Protection over the app's shared key ring
/// (DataProtectionExtensions: one ring for every replica, persisted in the database, guarded at startup), under a
/// purpose of its own. No new secret and no new configuration, and Data Protection rotates its keys while still
/// reading tokens made with older ones.
///
/// Payload: "{draftId:N}|{studioId:N}|{CreatedAt ticks}", protected, then base64url. Deliberately not a
/// time-limited protector: an expired time-limited payload fails to unprotect, which would make an expired link
/// look exactly like a forged one. The window is the draft's seven days, read from the token's own CreatedAt.
/// </summary>
public sealed class BookingDraftResumeTokens(IDataProtectionProvider provider) : IBookingDraftResumeTokens
{
    /// <summary>Changing this string invalidates every link already sent. Bump the version only on purpose.</summary>
    public const string Purpose = "BookingDraft.Resume.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    /// <summary>
    /// Mints the resume token for a draft whose CreatedAt is already whole seconds.
    /// </summary>
    /// <remarks>
    /// Why it throws rather than truncating: the token carries CreatedAt as .NET ticks (100 ns), and
    /// <see cref="BookingDraftResumeRef.Matches"/> compares those ticks exactly with the row as read back from the
    /// database. The column is datetime(6), so the seventh digit does not survive the round trip (rounded or
    /// truncated depending on the driver and sql_mode). A token minted from a sub-second value would never match its
    /// own row, and the link would 404 for its whole life. The caller truncates BEFORE saving
    /// (CreateGuestAppointmentCommand), so the tracked entity, the stored row and every later read agree. This guard
    /// catches a caller that forgot. Truncating here instead would produce a token for a value that was never
    /// stored. Do not relax it. Same rule as Waitlist.NotifiedAt and the waitlist claim token.
    /// </remarks>
    public string Mint(BookingDraft draft)
    {
        if (draft.CreatedAt.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new InvalidOperationException("A resume token needs the draft's CreatedAt in whole seconds.");

        string payload = string.Create(CultureInfo.InvariantCulture,
            $"{draft.Id:N}|{draft.StudioId:N}|{draft.CreatedAt.Ticks}");
        byte[] protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(payload));
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    public bool TryRead(string? token, out BookingDraftResumeRef resume)
    {
        resume = default;
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
            || !Guid.TryParseExact(parts[0], "N", out Guid draftId)
            || !Guid.TryParseExact(parts[1], "N", out Guid studioId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
        {
            return false;
        }

        resume = new BookingDraftResumeRef(draftId, studioId, ticks);
        return true;
    }
}
