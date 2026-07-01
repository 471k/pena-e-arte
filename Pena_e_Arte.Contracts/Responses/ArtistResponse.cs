namespace Pena_e_Arte.Contracts.Responses;

public record ArtistResponse(
    Guid          Id,
    Guid          StudioId,
    Guid?         UserId,
    string        FirstName,
    string        LastName,
    string        Email,
    string?       Specializations,
    decimal?      HourlyRate,
    bool          IsActive,
    string?       AvatarUrl,
    List<string>  PortfolioImages,
    string?       Slug,
    DateTime      CreatedAt,
    DateTime      UpdatedAt);
