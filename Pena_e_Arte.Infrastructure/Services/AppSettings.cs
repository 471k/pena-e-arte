using Microsoft.Extensions.Configuration;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class AppSettings(IConfiguration config) : IAppSettings
{
    // The config key exists (with an empty string) in appsettings.json, so a plain
    // `??` never catches an unset production deployment — only string.IsNullOrEmpty
    // does. Falling through silently would ship links like "/reset-password?..."
    // with no host at all.
    public string BaseUrl
    {
        get
        {
            string? value = config["App:BaseUrl"];
            return string.IsNullOrEmpty(value) ? "http://localhost:5173" : value;
        }
    }
}
