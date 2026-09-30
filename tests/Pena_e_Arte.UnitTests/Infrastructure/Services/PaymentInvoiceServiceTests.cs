using FluentAssertions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

/// <summary>
/// PaymentInvoiceService renders a real PDF via QuestPDF — there is no text-extraction library
/// in this repo (no new NuGet packages), so these tests exercise the MoneyText-based formatting
/// path for every currency shape (2/0/3 decimals) without crashing, rather than asserting on the
/// PDF's rendered text.
/// </summary>
public class PaymentInvoiceServiceTests
{
    private readonly PaymentInvoiceService _sut = new();

    [Theory]
    [InlineData("EUR")]
    [InlineData("ALL")]
    [InlineData("JPY")]
    [InlineData("KWD")]
    public void Generate_AnyCurrency_ProducesNonEmptyPdfBytes(string currency)
    {
        PaymentInvoiceData data = new(
            StudioName: "Test Studio",
            ClientFullName: "Jane Doe",
            ClientEmail: "jane@example.com",
            ArtistFullName: "Ink Master",
            AppointmentDate: DateTime.UtcNow,
            PaymentId: Guid.NewGuid(),
            TotalAmount: 12.345m,
            Method: "Card",
            Status: "Paid",
            ProviderReferenceId: "ref-123",
            CashNote: null,
            IssuedAt: DateTime.UtcNow,
            LineItems: [new InvoiceLineItem("Tattoo deposit", 12.345m)],
            Currency: currency);

        byte[] pdf = _sut.Generate(data);

        pdf.Should().NotBeEmpty();
        // "%PDF" magic bytes — confirms QuestPDF actually rendered rather than throwing.
        pdf.Take(4).Should().Equal(0x25, 0x50, 0x44, 0x46);
    }
}
