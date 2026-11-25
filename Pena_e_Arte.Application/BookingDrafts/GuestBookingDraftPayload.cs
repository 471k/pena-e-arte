using System.Globalization;
using System.Text.Json;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.Application.BookingDrafts;

/// <summary>
/// C05: a guest booking request written as the same draft JSON the signed-in booking form saves
/// (frontend/src/features/appointments/bookingDraft.ts, <c>draftPayload</c>), so #460's restore path reads it with
/// no second format. Each kept field is named one by one, never copied wholesale, and the safety notes are not one
/// of them; the caller still checks the result with <see cref="BookingDraftRules"/>, which stays the one exclusion.
/// </summary>
public static class GuestBookingDraftPayload
{
    /// <summary>The draft JSON, or null when it would not fit a draft (then the email goes out without a link).</summary>
    public static string? From(CreateAppointmentRequest booking, string? studioTimezone)
    {
        // The form's date field holds the studio's wall clock as "yyyy-MM-ddTHH:mm" (toStudioInputValue).
        string scheduledAt = TimezoneUtils.ToStudioLocalForRules(booking.Date, studioTimezone)
            .ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

        Dictionary<string, object?> content = new()
        {
            ["form"] = new Dictionary<string, object?>
            {
                ["artistId"] = booking.ArtistId?.ToString(),
                ["bookAnyArtist"] = booking.ArtistId is null,
                ["scheduledAt"] = scheduledAt,
                ["durationMinutes"] = booking.DurationMinutes,
                ["serviceId"] = booking.ServiceId?.ToString(),
                ["promoCode"] = booking.PromoCode ?? string.Empty,
                ["notes"] = booking.Notes ?? string.Empty,
            },
            ["intake"] = new Dictionary<string, object?>
            {
                ["tattooDescription"] = booking.TattooDescription,
                ["style"] = booking.Style ?? string.Empty,
                ["referralSource"] = booking.ReferralSource ?? string.Empty,
                ["referralSourceOther"] = booking.ReferralSourceOther ?? string.Empty,
            },
            ["desiredPlacement"] = booking.DesiredPlacementLocations ?? [],
            ["referralCode"] = booking.ReferralCode ?? string.Empty,
        };

        string payload = JsonSerializer.Serialize(content);
        return payload.Length <= BookingDraftRules.MaxPayloadLength ? payload : null;
    }
}
