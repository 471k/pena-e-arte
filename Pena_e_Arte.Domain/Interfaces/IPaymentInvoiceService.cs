namespace Pena_e_Arte.Domain.Interfaces;

public record InvoiceLineItem(string Label, decimal Amount);

public record PaymentInvoiceData(
    string StudioName,
    string ClientFullName,
    string ClientEmail,
    string ArtistFullName,
    DateTime AppointmentDate,
    Guid PaymentId,
    decimal TotalAmount,
    string Method,
    string Status,
    string? ProviderReferenceId,
    DateTime IssuedAt,
    IReadOnlyList<InvoiceLineItem> LineItems,
    // The studio's ShowPlatformBranding flag (plan-gated removal). Receipts honour it exactly as consent
    // PDFs do (SP-03). Deliberately not defaulted, so a new construction site has to decide it.
    // There is no cash-note field: a cash note is free text that staff can write as readily as the
    // client (DeclareCashDeposit is ClientAndAbove), so it stays on the owner's payment detail page and never
    // reaches the client's receipt.
    bool ShowPlatformBranding,
    string Currency = "",
    // Lek reference frozen when the client paid (Albanian EUR studios only; all null otherwise). The
    // receipt shows each amount's own lek at this rate and an "Exchange rate" row (spec D7).
    string? ReferenceCurrency = null,
    decimal? ReferenceRate = null,
    DateOnly? ReferenceRateDate = null);

public interface IPaymentInvoiceService
{
    byte[] Generate(PaymentInvoiceData data);
}
