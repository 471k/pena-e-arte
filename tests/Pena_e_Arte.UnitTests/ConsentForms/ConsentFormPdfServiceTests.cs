using FluentAssertions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class ConsentFormPdfServiceTests
{
    private static ConsentFormPdfData MakeData(bool showPlatformBranding) => new(
        StudioName:           "Test Studio",
        ClientFullName:       "Ana Costa",
        ArtistFullName:       "João Silva",
        AppointmentDate:      DateTime.UtcNow,
        SignatureText:        "Ana Costa",
        SignedAt:             DateTime.UtcNow,
        ShowPlatformBranding: showPlatformBranding);

    [Fact]
    public void Generate_ReturnsNonEmptyPdf_WhenShowPlatformBrandingIsTrue()
    {
        ConsentFormPdfService svc = new();
        byte[] pdf = svc.Generate(MakeData(showPlatformBranding: true));
        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void Generate_ReturnsNonEmptyPdf_WhenShowPlatformBrandingIsFalse()
    {
        ConsentFormPdfService svc = new();
        byte[] pdf = svc.Generate(MakeData(showPlatformBranding: false));
        pdf.Should().NotBeEmpty();
    }
}
