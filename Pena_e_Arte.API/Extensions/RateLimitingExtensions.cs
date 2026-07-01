using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Pena_e_Arte.API.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opt.AddFixedWindowLimiter("auth", o =>
            {
                o.Window                = TimeSpan.FromMinutes(1);
                o.PermitLimit           = 10;
                o.QueueProcessingOrder  = QueueProcessingOrder.OldestFirst;
                o.QueueLimit            = 0;
                o.AutoReplenishment     = true;
            });

            opt.AddFixedWindowLimiter("public-write", o =>
            {
                o.Window                = TimeSpan.FromMinutes(1);
                o.PermitLimit           = 30;
                o.QueueProcessingOrder  = QueueProcessingOrder.OldestFirst;
                o.QueueLimit            = 0;
                o.AutoReplenishment     = true;
            });
        });

        return services;
    }
}
