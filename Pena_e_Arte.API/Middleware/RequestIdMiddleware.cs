using Serilog.Context;

namespace Pena_e_Arte.API.Middleware;

/// <summary>
/// Pushes <c>request_id</c> onto the Serilog LogContext so every log line written
/// during this request — not just the final HTTP-summary line — carries it. Must be
/// registered before any other middleware so the scope wraps the whole pipeline.
/// </summary>
public class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using (LogContext.PushProperty("request_id", context.TraceIdentifier))
            await next(context);
    }
}
