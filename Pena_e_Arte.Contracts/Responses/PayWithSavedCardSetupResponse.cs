namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Everything the frontend needs to call the POK SDK's `payByCardToken` — field names
/// mirror its `PayerAuthentication` TypeScript type so the frontend can pass this straight
/// through with no remapping. When Status is already "Captured" or "Paid" (the deposit was
/// already settled — same reconciliation CreateDepositPaymentCommand does for a new-card
/// checkout), the four setup fields are null: no 3DS setup was needed or performed.</summary>
public record PayWithSavedCardSetupResponse(
    Guid PaymentId,
    string Status,
    string? OrderId,
    string? CardTokenId,
    string? PayerAuthSetupReferenceId,
    PayWithSavedCardDeviceDataCollection? DeviceDataCollection);

public record PayWithSavedCardDeviceDataCollection(string Url, string AccessToken);
