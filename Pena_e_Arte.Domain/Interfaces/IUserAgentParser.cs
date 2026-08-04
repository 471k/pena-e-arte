namespace Pena_e_Arte.Domain.Interfaces;

public interface IUserAgentParser
{
    (string? DeviceType, string? Browser, string? Os) Parse(string? userAgent);
}
