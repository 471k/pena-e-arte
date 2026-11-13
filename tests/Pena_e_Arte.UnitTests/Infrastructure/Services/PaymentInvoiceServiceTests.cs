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
            IssuedAt: DateTime.UtcNow,
            LineItems: [new InvoiceLineItem("Tattoo deposit", 12.345m)],
            ShowPlatformBranding: true,
            Currency: currency);

        byte[] pdf = _sut.Generate(data);

        pdf.Should().NotBeEmpty();
        // "%PDF" magic bytes — confirms QuestPDF actually rendered rather than throwing.
        pdf.Take(4).Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    // ── Lek reference (Albanian EUR studios, spec D7) ────────────────────────

    private static PaymentInvoiceData EuroReceipt(decimal? rate, bool showPlatformBranding = true) => new(
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
        IssuedAt: DateTime.UtcNow,
        LineItems: [new InvoiceLineItem("Session part 1", 50.5m), new InvoiceLineItem("Session part 2", 29.5m)],
        ShowPlatformBranding: showPlatformBranding,
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

    // ── Payment details rows ─────────────────────────────────────────────────

    [Fact]
    public void PaymentDetailRows_LabelTheProviderReferenceGenerically_NeverAsStripe()
    {
        IReadOnlyList<(string Label, string Value)> rows = PaymentInvoiceService.PaymentDetailRows(EuroReceipt(null));

        rows.Should().Equal(("Method", "Card"), ("Status", "Paid"), ("Payment reference", "ref-123"));
        rows.Should().NotContain(r => r.Label.Contains("Stripe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PaymentDetailRows_WithoutAProviderReference_OmitTheRow() =>
        PaymentInvoiceService.PaymentDetailRows(EuroReceipt(null) with { ProviderReferenceId = null })
            .Select(r => r.Label).Should().Equal("Method", "Status");

    [Fact]
    public void PaymentDetailRows_WithFrozenReference_EndWithTheExchangeRate() =>
        PaymentInvoiceService.PaymentDetailRows(EuroReceipt(90.74m)).Select(r => r.Label)
            .Should().Equal("Method", "Status", "Payment reference", "Exchange rate");

    [Fact]
    public void PaymentDetailRows_NeverIncludeAFreeTextNote() =>
        // The cash note is staff-writable free text and is no longer carried to the receipt at all;
        // this pins that no row reintroduces it under another label.
        PaymentInvoiceService.PaymentDetailRows(EuroReceipt(90.74m)).Select(r => r.Label)
            .Should().NotContain(l => l.Contains("Note", StringComparison.OrdinalIgnoreCase));

    // ── Platform branding (SP-03, extended to receipts) ──────────────────────

    [Fact]
    public void PlatformBrandingLine_WhenTheStudioShowsBranding_IsTheConsentPdfsLine() =>
        PaymentInvoiceService.PlatformBrandingLineText(EuroReceipt(null, showPlatformBranding: true))
            .Should().Be("Generated via TattooOS · tattooos.co");

    [Fact]
    public void PlatformBrandingLine_WhenTheStudioHasRemovedBranding_IsAbsent() =>
        PaymentInvoiceService.PlatformBrandingLineText(EuroReceipt(null, showPlatformBranding: false))
            .Should().BeNull();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generate_EitherBrandingSetting_RendersAPdf(bool showPlatformBranding) =>
        _sut.Generate(EuroReceipt(null, showPlatformBranding)).Take(4).Should().Equal(0x25, 0x50, 0x44, 0x46);
}
