using Pena_e_Arte.Domain.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pena_e_Arte.Infrastructure.Services;

public class ConsentFormPdfService : IConsentFormPdfService
{
    static ConsentFormPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ConsentFormPdfData d)
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
                    col.Spacing(14);

                    // ── Header ───────────────────────────────────────────────
                    col.Item().Text(d.StudioName)
                        .FontSize(18).Bold().FontColor(Colors.Black);

                    col.Item().Text("TATTOO CONSENT FORM")
                        .FontSize(13).Bold().FontColor("#444444");

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Details table ────────────────────────────────────────
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(9);
                        });

                        void Row(string label, string value)
                        {
                            table.Cell().PaddingVertical(2).Text(label).Bold().FontColor("#666666");
                            table.Cell().PaddingVertical(2).Text(value);
                        }

                        Row("Client",        d.ClientFullName);
                        Row("Artist",        d.ArtistFullName);
                        Row("Appointment",   d.AppointmentDate.ToString("dddd d MMMM yyyy, HH:mm") + " UTC");
                        Row("Document date", d.SignedAt.ToString("d MMMM yyyy, HH:mm:ss") + " UTC");
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Consent text ─────────────────────────────────────────
                    col.Item().Text("Declaration of Informed Consent").FontSize(11).Bold();

                    col.Item().Text(text =>
                    {
                        text.Span(
                            "I, the undersigned, voluntarily consent to receive a tattoo from " +
                            $"{d.StudioName}. I confirm that I:\n\n" +
                            "• am 18 years of age or older (or have parental/guardian consent if a minor);\n" +
                            "• am not under the influence of alcohol or controlled substances;\n" +
                            "• do not have a skin condition, blood disorder, or other medical condition that " +
                            "would contraindicate tattooing without prior written medical clearance;\n" +
                            "• have been fully informed of the procedure, aftercare requirements, and risks " +
                            "including but not limited to infection, allergic reaction, scarring, and fading;\n" +
                            "• understand that tattoos are permanent and that touch-up results may vary;\n" +
                            "• release " + d.StudioName + " and its artists from liability for any adverse " +
                            "reactions arising from accurate disclosure of my health status;\n" +
                            "• agree to follow all aftercare instructions provided.\n\n" +
                            "By signing below I acknowledge that I have read, understand, and agree to all " +
                            "of the above terms."
                        );
                    });

                    col.Item().LineHorizontal(1).LineColor("#cccccc");

                    // ── Signature block ──────────────────────────────────────
                    col.Item().Text("Digital Signature").FontSize(11).Bold();

                    col.Item().Border(1).BorderColor("#cccccc").Padding(12).Column(sig =>
                    {
                        sig.Item().Text(d.SignatureText)
                            .FontSize(16).Italic().FontColor(Colors.Black);
                        sig.Item().PaddingTop(6).Text(
                            $"Signed digitally on {d.SignedAt:d MMMM yyyy} at {d.SignedAt:HH:mm:ss} UTC")
                            .FontSize(8).FontColor("#888888");
                    });

                    // ── Footer note ──────────────────────────────────────────
                    col.Item().PaddingTop(8).Text(
                        "This document was generated automatically by Pena e Artë Studio Platform " +
                        "and is a legally binding digital consent record.")
                        .FontSize(8).FontColor("#aaaaaa").Italic();

                    // SP-03: show "Generated via" line only when studio has branding enabled.
                    if (d.ShowPlatformBranding)
                    {
                        col.Item().AlignRight().Text("Generated via Pena e Artë · penaearte.com")
                            .FontSize(8).FontColor("#bbbbbb").Italic();
                    }
                });
            });
        }).GeneratePdf();
    }
}
