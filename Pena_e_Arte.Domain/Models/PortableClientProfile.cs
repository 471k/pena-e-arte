namespace Pena_e_Arte.Domain.Models;

public record PortableClientProfile(
    string DisplayName,
    IReadOnlyList<string> BodyMapLocations,
    IReadOnlyList<PortableTattooRecord> TattooHistory);
