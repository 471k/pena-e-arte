namespace Pena_e_Arte.Contracts.Responses;

/// <summary>GET /studios/me/pay-model (A07a, OwnerOnly): the studio's default terms.</summary>
/// <param name="Arrangement">"Commission", "BoothRent", "FlatFeePerSession" or "Salaried".</param>
/// <param name="CommissionRate">Percent to the artist (60.00 = 60%). Null = not set.</param>
/// <param name="FlatFeePerSession">The studio's cut per session. Null = not set.</param>
/// <param name="ReviewedAt">When the owner saved or confirmed it. Null = set for them, not reviewed.</param>
/// <param name="Currency">The studio currency the flat fee is in.</param>
public record StudioPayModelResponse(
    string Arrangement,
    decimal? CommissionRate,
    decimal? FlatFeePerSession,
    DateTime? ReviewedAt,
    string Currency);

/// <summary>
/// GET /artists/{id}/pay-model (A07a, OwnerOnly): one artist's own terms (null fields use the studio's).
/// Deliberately separate from ArtistResponse, which clients can read.
/// </summary>
public record ArtistPayModelResponse(
    Guid ArtistId,
    string? Arrangement,
    decimal? CommissionRate,
    decimal? FlatFeePerSession);
