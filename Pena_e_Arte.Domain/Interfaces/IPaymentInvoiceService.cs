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
    string? CashNote,
    DateTime IssuedAt,
    IReadOnlyList<InvoiceLineItem> LineItems,
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
