using FluentAssertions;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Studios;

public class QrCodeServiceTests
{
    private readonly QrCodeService _sut = new();

    [Fact]
    public void GeneratePng_ValidUrl_ReturnsNonEmptyByteArray()
    {
        byte[] result = _sut.GeneratePng("https://penaearte.com/s/test-studio");

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GeneratePng_ContainsPngSignature()
    {
        byte[] result = _sut.GeneratePng("https://penaearte.com/s/test-studio");

        // PNG files start with the 8-byte signature: 137 80 78 71 13 10 26 10
        result[0].Should().Be(137);
        result[1].Should().Be(80);  // 'P'
        result[2].Should().Be(78);  // 'N'
        result[3].Should().Be(71);  // 'G'
    }

    [Fact]
    public void GenerateSvg_ValidUrl_ReturnsNonEmptyString()
    {
        string result = _sut.GenerateSvg("https://penaearte.com/s/test-studio");

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateSvg_ContainsSvgElement()
    {
        string result = _sut.GenerateSvg("https://penaearte.com/s/test-studio");

        result.Should().Contain("<svg");
    }

    [Fact]
    public void GeneratePng_CustomPixelSize_ReturnsDifferentSizeThanDefault()
    {
        byte[] smallPng  = _sut.GeneratePng("https://penaearte.com/s/test", pixelSize: 5);
        byte[] largePng  = _sut.GeneratePng("https://penaearte.com/s/test", pixelSize: 40);

        largePng.Length.Should().BeGreaterThan(smallPng.Length);
    }
}
