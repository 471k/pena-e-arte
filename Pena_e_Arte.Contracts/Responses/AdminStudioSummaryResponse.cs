namespace Pena_e_Arte.Contracts.Responses;

public record AdminStudioSummaryResponse(
    string OwnerEmail,
    string OwnerDisplayName,
    int ArtistCount,
    int ClientCount,
    int AppointmentCount);
