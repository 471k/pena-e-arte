namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// PUT /studios/me/pay-model (A07a): how the studio's artists are paid unless an artist has their own.
/// </summary>
/// <param name="Arrangement">"Commission", "BoothRent", "FlatFeePerSession" or "Salaried".</param>
/// <param name="CommissionRate">Percent to the artist, 0–100, at most 2 decimals. Required for Commission.</param>
/// <param name="FlatFeePerSession">The studio's cut per session in the studio currency. Required for FlatFeePerSession.</param>
public record UpdateStudioPayModelRequest(string Arrangement, decimal? CommissionRate, decimal? FlatFeePerSession);

/// <summary>
/// PUT /artists/{id}/pay-model (A07a, OwnerOnly): one artist's own terms. Every field is optional; a
/// null field means "use the studio's".
/// </summary>
/// <param name="Arrangement">One of the four arrangements, or null for the studio's.</param>
/// <param name="CommissionRate">Percent to the artist, 0–100, at most 2 decimals, or null for the studio's.</param>
/// <param name="FlatFeePerSession">The studio's cut per session for this artist, or null for the studio's.</param>
public record UpdateArtistPayModelRequest(string? Arrangement, decimal? CommissionRate, decimal? FlatFeePerSession);
