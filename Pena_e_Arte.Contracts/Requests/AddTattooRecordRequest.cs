namespace Pena_e_Arte.Contracts.Requests;

public record AddTattooRecordRequest(
    Guid         ArtistId,
    Guid?        AppointmentId,
    string       Description,
    string       BodyLocation,
    List<string> PhotoUrls,
    DateTime     CompletedAt);
