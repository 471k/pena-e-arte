namespace Pena_e_Arte.Contracts.Requests;

public record UpdateTattooRecordRequest(
    string       Description,
    string       BodyLocation,
    List<string> PhotoUrls,
    DateTime     CompletedAt);
