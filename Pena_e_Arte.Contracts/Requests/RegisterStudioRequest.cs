namespace Pena_e_Arte.Contracts.Requests;

public record RegisterStudioRequest(
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    string OwnerEmail,
    string Nipt,
    string? ReferralCode = null);
