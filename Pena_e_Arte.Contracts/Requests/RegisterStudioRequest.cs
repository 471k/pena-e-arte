namespace Pena_e_Arte.Contracts.Requests;

public record RegisterStudioRequest(
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    string OwnerEmail,
    string Nipt,
    string AddressLine1,
    string? AddressLine2 = null,
    string? PostalCode = null,
    string? ReferralCode = null);
