namespace Pena_e_Arte.Contracts.Responses;

public record PackageResponse(
    Guid Id, Guid StudioId, string Name, int SessionCount, decimal Price, bool IsActive, DateTime CreatedAt);

public record PackagePurchaseResponse(
    Guid Id,
    Guid StudioId,
    Guid PackageId,
    string? PackageName,
    Guid ClientId,
    int SessionsRemaining,
    DateTime CreatedAt);

public record PurchasePackageResponse(Guid PackagePurchaseId, string? ClientToken);
