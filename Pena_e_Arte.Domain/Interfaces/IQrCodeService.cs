namespace Pena_e_Arte.Domain.Interfaces;

public interface IQrCodeService
{
    byte[] GeneratePng(string url, int pixelSize = 20);
    string GenerateSvg(string url);
}
