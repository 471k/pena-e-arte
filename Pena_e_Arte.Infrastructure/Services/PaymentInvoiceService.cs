using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pena_e_Arte.Infrastructure.Services;

public class PaymentInvoiceService : IPaymentInvoiceService
{
    static PaymentInvoiceService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentInvoiceData d)
    {
        // Albanian EUR studios: each amount also shows its own lek at the frozen rate (never a sum of
        // converted lines), so the amount columns need more room.
        bool hasReference = d.ReferenceRate is not null && d.ReferenceCurrency is not null;
        float amountWidth = hasReference ? 170 : 100;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Arial));

                page.Content().Column(col =>
                {
                    col.Spacing(16);

                    // ── Studio + receipt header ─────────────────────────────
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(d.StudioName).FontSize(20).Bold();

                        row.ConstantItem(140).Column(right =>
                        {
                            right.Item().AlignRight()
                                .Text("RECEIPT").FontSize(15).Bold().FontColor("#444444");
                            right.Item().AlignRight()
                                .Text($"#{d.PaymentId.ToString("N")[..8].ToUpper()}")
                                .FontSize(8).FontColor("#888888");
                        });
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Billed to / meta ────────────────────────────────────
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });

                        table.Cell().Column(left =>
                        {
                            left.Item().Text("BILLED TO")
                                .FontSize(8).Bold().FontColor("#888888");
                            left.Item().PaddingTop(4).Text(d.ClientFullName).Bold();
                            if (!string.IsNullOrEmpty(d.ClientEmail))
                                left.Item().Text(d.ClientEmail).FontSize(9).FontColor("#555555");
                        });

                        table.Cell().Column(right =>
                        {
                            void MetaRow(string label, string value)
                            {
                                right.Item().PaddingBottom(2).Row(r =>
                                {
                                    r.RelativeItem().Text(label)
                                        .FontSize(8).Bold().FontColor("#888888");
                                    r.RelativeItem().AlignRight().Text(value).FontSize(9);
                                });
                            }

                            MetaRow("ARTIST", d.ArtistFullName);
                            MetaRow("APPOINTMENT", d.AppointmentDate.ToString("d MMM yyyy, HH:mm") + " UTC");
                            MetaRow("ISSUED", d.IssuedAt.ToString("d MMM yyyy") + " UTC");
                        });
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Line items ──────────────────────────────────────────
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.ConstantColumn(amountWidth);
                        });

                        // Header
                        table.Cell().Background("#f5f5f5").Padding(6)
                            .Text("Description").Bold().FontSize(9);
                        table.Cell().Background("#f5f5f5").Padding(6)
                            .Text("Amount").Bold().FontSize(9).AlignRight();

                        // Rows
                        foreach (InvoiceLineItem line in d.LineItems)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor("#eeeeee").Padding(6)
                                .Text(line.Label);
                            table.Cell().BorderBottom(0.5f).BorderColor("#eeeeee").Padding(6)
                                .Text(FormatAmount(line.Amount, d)).AlignRight();
                        }
                    });

                    // ── Total ───────────────────────────────────────────────
                    col.Item().AlignRight().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(80);
                            cols.ConstantColumn(amountWidth);
                        });

                        table.Cell().Background("#111111").Padding(8)
                            .Text("TOTAL").Bold().FontSize(10).FontColor("#ffffff");
                        table.Cell().Background("#111111").Padding(8)
                            .Text(FormatAmount(d.TotalAmount, d)).Bold().FontSize(10)
                            .FontColor("#ffffff").AlignRight();
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Payment details ─────────────────────────────────────
                    col.Item().Text("Payment details").Bold().FontSize(9).FontColor("#444444");

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(9);
                        });

                        foreach ((string label, string value) in PaymentDetailRows(d))
                        {
                            table.Cell().PaddingVertical(2)
                                .Text(label).FontSize(9).FontColor("#888888");
                            table.Cell().PaddingVertical(2).Text(value).FontSize(9);
                        }
                    });

                    // ── Footer ──────────────────────────────────────────────
                    col.Item().PaddingTop(24)
                        .Text("Thank you for your business. This document is your official receipt.")
                        .FontSize(8).FontColor("#aaaaaa").Italic();

                    // Platform branding, on the studio's ShowPlatformBranding flag: the consent PDF's
                    // line and styling (SP-03), since a receipt is a document, not the widget.
                    if (PlatformBrandingLineText(d) is string brandingLine)
                    {
                        col.Item().AlignRight().Text(brandingLine)
                            .FontSize(8).FontColor("#bbbbbb").Italic();
                    }
                });
            });
        }).GeneratePdf();
    }

    // The receipt's text decisions, kept out of the QuestPDF layout so tests can check them (the repo has
    // no PDF text-extraction library, and no new package may be added for one).
    internal static string FormatAmount(decimal amount, PaymentInvoiceData d)
        => d.ReferenceRate is decimal rate
            ? MoneyText.FormatWithReference(amount, d.Currency, ReferenceAmount.ToWholeLek(amount, rate),
                d.ReferenceCurrency, approximate: false)
            : MoneyText.Format(amount, d.Currency);

    internal static string? ExchangeRateRowText(PaymentInvoiceData d)
        => d is { ReferenceRate: decimal rate, ReferenceRateDate: DateOnly rateDate }
            ? MoneyText.BankOfAlbaniaRateLine(rate, rateDate)
            : null;

    // The "Payment details" rows, in order. The provider reference is labelled generically: it holds
    // the provider's order id (POK today), which a client disputing a charge with their bank can quote,
    // and naming a provider the studio may not use would be wrong. No free-text note row: see
    // PaymentInvoiceData.
    internal static IReadOnlyList<(string Label, string Value)> PaymentDetailRows(PaymentInvoiceData d)
    {
        List<(string Label, string Value)> rows = [("Method", d.Method), ("Status", d.Status)];
        if (!string.IsNullOrEmpty(d.ProviderReferenceId))
            rows.Add(("Payment reference", d.ProviderReferenceId));
        if (ExchangeRateRowText(d) is string rateRow)
            rows.Add(("Exchange rate", rateRow));
        return rows;
    }

    internal static string? PlatformBrandingLineText(PaymentInvoiceData d)
        => d.ShowPlatformBranding ? DocumentBranding.GeneratedViaLine : null;
}
