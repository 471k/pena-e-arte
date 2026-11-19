namespace Pena_e_Arte.Contracts.Requests;

/// <summary>Admin subscription cancel (slice 8 phase 3, D03). Override is null for the default (formula) path;
/// "AdminFull"/"AdminNone" require Reason. The Confirmed* fields are the quote the admin reviewed
/// (GET /platform/studios/{id}/subscription/cancel-quote): the server rebuilds it and refuses with 409 when it
/// no longer matches, so the consequence shown is the consequence applied. Override stays a string so an unknown
/// value is a validation failure (422), not an exception in the endpoint.</summary>
public record CancelSubscriptionRequest(
    string? Override,
    string? Reason,
    string ConfirmedStatus,
    Guid? ConfirmedInvoiceId,
    decimal ConfirmedRefundAmount);
