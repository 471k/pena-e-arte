namespace Pena_e_Arte.Contracts.Responses;

public record TattooRecordResponse(
    Guid         Id,
    Guid         ClientId,
    Guid         ArtistId,
    Guid?        AppointmentId,
    string       Description,
    string       BodyLocation,
    List<string> PhotoUrls,
    DateTime     CompletedAt,
    DateTime     CreatedAt);
