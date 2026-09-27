namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Currency is null for a country RegionInfo doesn't recognise as having a currency this
/// platform lists — the caller then asks the owner to pick one explicitly.</summary>
public record CountryDefaultCurrencyResponse(string CountryCode, string? Currency);
