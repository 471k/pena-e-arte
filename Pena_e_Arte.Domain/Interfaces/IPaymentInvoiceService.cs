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
    IReadOnlyList<InvoiceLineItem> LineItems);

public interface IPaymentInvoiceService
{
    byte[] Generate(PaymentInvoiceData data);
}
