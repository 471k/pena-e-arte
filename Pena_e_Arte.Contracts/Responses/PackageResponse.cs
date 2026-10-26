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
    DateTime CreatedAt,
    decimal Amount,
    string Currency,
    // Lek reference frozen at purchase (Albanian EUR studios only; all null otherwise).
    decimal? ReferenceAmount = null,
    string? ReferenceCurrency = null,
    decimal? ReferenceRate = null,
    DateOnly? ReferenceRateDate = null);

public record PurchasePackageResponse(Guid PackagePurchaseId, string? ClientToken);
