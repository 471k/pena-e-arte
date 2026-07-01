using Microsoft.Extensions.Configuration;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class AppSettings(IConfiguration config) : IAppSettings
{
    public string BaseUrl => config["App:BaseUrl"] ?? string.Empty;
}
