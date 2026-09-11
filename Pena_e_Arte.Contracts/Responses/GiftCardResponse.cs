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
    DateTime CreatedAt);

/// <summary>Public lookup shape — deliberately excludes PurchaserEmail/RecipientEmail
/// (enumeration-risk endpoint; see architecture.md AllowAnonymous Exceptions table).</summary>
public record GiftCardBalanceResponse(decimal RemainingBalance, string Status);

public record PurchaseGiftCardResponse(Guid GiftCardId, string? ClientToken, string Status);
