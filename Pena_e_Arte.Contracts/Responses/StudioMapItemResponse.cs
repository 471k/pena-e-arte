namespace Pena_e_Arte.Contracts.Responses;

public record StudioMapItemResponse(
    Guid   Id,
    string Name,
    string Slug,
    double Latitude,
    double Longitude,
    string City);
