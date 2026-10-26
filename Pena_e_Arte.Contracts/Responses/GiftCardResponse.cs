namespace Pena_e_Arte.Contracts.Responses;

public record GiftCardResponse(
    Guid Id,
    Guid StudioId,
    string Code,
    decimal InitialBalance,
    decimal RemainingBalance,
    string PurchaserEmail,
    string? RecipientEmail,
    string Status,
    DateTime CreatedAt,
    string Currency,
    // Lek reference frozen at purchase (Albanian EUR studios only; all null otherwise). The balance's lek
    // is shown at this rate on staff screens (spec Q3, decided 2026-10-26).
    decimal? ReferenceAmount = null,
    string? ReferenceCurrency = null,
    decimal? ReferenceRate = null,
    DateOnly? ReferenceRateDate = null);

/// <summary>Public lookup shape — deliberately excludes PurchaserEmail/RecipientEmail
/// (enumeration-risk endpoint; see architecture.md AllowAnonymous Exceptions table).</summary>
public record GiftCardBalanceResponse(decimal RemainingBalance, string Status);

public record PurchaseGiftCardResponse(Guid GiftCardId, string? ClientToken, string Status);
