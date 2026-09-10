namespace Pena_e_Arte.Contracts.Requests;

public record PurchaseGiftCardRequest(
    string StudioSlug,
    decimal Amount,
    string PurchaserEmail,
    string? RecipientEmail);
