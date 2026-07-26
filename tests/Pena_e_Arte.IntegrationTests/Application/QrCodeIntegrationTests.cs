using FluentAssertions;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class QrCodeIntegrationTests(DatabaseFixture fixture)
{
    // ── GetStudioQrCode — PNG ────────────────────────────────────────────────────

    [Fact]
    public async Task GetQrCode_Png_ReturnsNonEmptyPngBytes()
    {
        Studio studio = await SeedStudio();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        QrCodeService qrService = new();
        GetStudioQrCodeHandler handler = new(db, qrService);

        QrCodeResponse result = await handler.Handle(
            new GetStudioQrCodeQuery(studio.Id, "png"), default);

        result.ContentType.Should().Be("image/png");
        result.Data.Should().NotBeNullOrEmpty();
        // PNG header magic bytes
        result.Data[0].Should().Be(137);
        result.Data[1].Should().Be(80); // 'P'
    }

    [Fact]
    public async Task GetQrCode_Svg_ReturnsSvgContent()
    {
        Studio studio = await SeedStudio();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        QrCodeService qrService = new();
        GetStudioQrCodeHandler handler = new(db, qrService);

        QrCodeResponse result = await handler.Handle(
            new GetStudioQrCodeQuery(studio.Id, "svg"), default);

        result.ContentType.Should().Be("image/svg+xml");
        result.Data.Should().NotBeNullOrEmpty();
        System.Text.Encoding.UTF8.GetString(result.Data).Should().Contain("<svg");
    }

    [Fact]
    public async Task GetQrCode_InactiveStudio_ThrowsNotFoundException()
    {
        Studio studio = await SeedStudio(isActive: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        QrCodeService qrService = new();
        GetStudioQrCodeHandler handler = new(db, qrService);

        Func<Task> act = () => handler.Handle(new GetStudioQrCodeQuery(studio.Id, "png"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetQrCode_UnknownStudio_ThrowsNotFoundException()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        QrCodeService qrService = new();
        GetStudioQrCodeHandler handler = new(db, qrService);

        Func<Task> act = () => handler.Handle(new GetStudioQrCodeQuery(Guid.NewGuid(), "png"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<Studio> SeedStudio(bool isActive = true)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "QR Test Studio",
            Slug = "qr-test-" + Guid.NewGuid().ToString("N")[..8],
            City = "Lisboa",
            IsActive = isActive,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();
        return studio;
    }
}
