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
    string? ReferralCode = null,
    /// <summary>ISO 3166-1 alpha-2, e.g. "AL". Required by RegisterStudioValidator (not the type
    /// itself, to keep this positional record source-compatible) — prefilled from the geocoded
    /// address on the frontend.</summary>
    string? CountryCode = null,
    /// <summary>ISO 4217. Null defaults to CountryCode's currency (RegisterStudioValidator
    /// requires that default to exist when this is omitted).</summary>
    string? Currency = null,
    /// <summary>IANA time zone id, e.g. "Europe/Lisbon". Null keeps the studio default
    /// (Europe/Tirane) — old clients send none; the registration form prefills it from the
    /// visitor's location.</summary>
    string? Timezone = null);
