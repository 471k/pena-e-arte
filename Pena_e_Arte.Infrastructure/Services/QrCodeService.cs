using Pena_e_Arte.Domain.Interfaces;
using QRCoder;

namespace Pena_e_Arte.Infrastructure.Services;

public class QrCodeService : IQrCodeService
{
    public byte[] GeneratePng(string url, int pixelSize = 20)
    {
        QRCodeGenerator generator = new();
        QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        PngByteQRCode code = new(data);
        return code.GetGraphic(pixelSize);
    }

    public string GenerateSvg(string url)
    {
        QRCodeGenerator generator = new();
        QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        SvgQRCode code = new(data);
        return code.GetGraphic(20);
    }
}
