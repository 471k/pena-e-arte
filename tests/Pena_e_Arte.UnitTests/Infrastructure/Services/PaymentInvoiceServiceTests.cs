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

    // ── Lek reference (Albanian EUR studios, spec D7) ────────────────────────

    private static PaymentInvoiceData EuroReceipt(decimal? rate) => new(
        StudioName: "Test Studio",
        ClientFullName: "Jane Doe",
        ClientEmail: "jane@example.com",
        ArtistFullName: "Ink Master",
        AppointmentDate: DateTime.UtcNow,
        PaymentId: Guid.NewGuid(),
        TotalAmount: 80m,
        Method: "Card",
        Status: "Paid",
        ProviderReferenceId: "ref-123",
        CashNote: null,
        IssuedAt: DateTime.UtcNow,
        LineItems: [new InvoiceLineItem("Session part 1", 50.5m), new InvoiceLineItem("Session part 2", 29.5m)],
        Currency: "EUR",
        ReferenceCurrency: rate is null ? null : "ALL",
        ReferenceRate: rate,
        ReferenceRateDate: rate is null ? null : new DateOnly(2026, 10, 23));

    [Fact]
    public void Generate_WithFrozenReference_RendersAPdf()
    {
        byte[] pdf = _sut.Generate(EuroReceipt(90.74m));

        pdf.Take(4).Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    [Fact]
    public void Amounts_WithFrozenReference_EachAmountIsConvertedOnItsOwn()
    {
        PaymentInvoiceData d = EuroReceipt(90.74m);

        PaymentInvoiceService.FormatAmount(50.5m, d).Should().Be("50.50 EUR (4,582 ALL)"); // 4,582.37
        PaymentInvoiceService.FormatAmount(29.5m, d).Should().Be("29.50 EUR (2,677 ALL)"); // 2,676.83
        // The total comes from the euro total (7,259.20 -> 7,259), never from summing the lines (7,259
        // here by coincidence, but the rule matters when line roundings add up differently).
        PaymentInvoiceService.FormatAmount(80m, d).Should().Be("80.00 EUR (7,259 ALL)");
    }

    [Fact]
    public void ExchangeRateRow_WithFrozenReference_StatesTheRateAndItsDate() =>
        PaymentInvoiceService.ExchangeRateRowText(EuroReceipt(90.74m))
            .Should().Be("1 EUR = 90.74 ALL, Bank of Albania official rate of 23 Oct 2026");

    [Fact]
    public void WithoutReference_AmountsAndRowsAreExactlyAsBefore()
    {
        PaymentInvoiceData d = EuroReceipt(null);

        PaymentInvoiceService.FormatAmount(50.5m, d).Should().Be("50.50 EUR");
        PaymentInvoiceService.ExchangeRateRowText(d).Should().BeNull();
    }
}
