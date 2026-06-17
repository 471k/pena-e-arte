using System.Globalization;
using Pena_e_Arte.Domain.Interfaces;
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

                            MetaRow("ARTIST",      d.ArtistFullName);
                            MetaRow("APPOINTMENT", d.AppointmentDate.ToString("d MMM yyyy, HH:mm") + " UTC");
                            MetaRow("ISSUED",      d.IssuedAt.ToString("d MMM yyyy") + " UTC");
                        });
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Line items ──────────────────────────────────────────
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.ConstantColumn(100);
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
                                .Text(FormatCurrency(line.Amount)).AlignRight();
                        }
                    });

                    // ── Total ───────────────────────────────────────────────
                    col.Item().AlignRight().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(80);
                            cols.ConstantColumn(100);
                        });

                        table.Cell().Background("#111111").Padding(8)
                            .Text("TOTAL").Bold().FontSize(10).FontColor("#ffffff");
                        table.Cell().Background("#111111").Padding(8)
                            .Text(FormatCurrency(d.TotalAmount)).Bold().FontSize(10)
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

                        void Row(string label, string value)
                        {
                            table.Cell().PaddingVertical(2)
                                .Text(label).FontSize(9).FontColor("#888888");
                            table.Cell().PaddingVertical(2).Text(value).FontSize(9);
                        }

                        Row("Method",  d.Method);
                        Row("Status",  d.Status);
                        if (d.StripePaymentIntentId is not null)
                            Row("Stripe PI", d.StripePaymentIntentId);
                        if (d.CashNote is not null)
                            Row("Note", d.CashNote);
                    });

                    // ── Footer ──────────────────────────────────────────────
                    col.Item().PaddingTop(24)
                        .Text("Thank you for your business. This document is your official receipt.")
                        .FontSize(8).FontColor("#aaaaaa").Italic();
                });
            });
        }).GeneratePdf();
    }

    private static string FormatCurrency(decimal amount)
        => amount.ToString("C2", new CultureInfo("pt-PT"));
}
