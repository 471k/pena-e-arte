using System.Diagnostics;
using Serilog.Context;

namespace Pena_e_Arte.API.Middleware;

/// <summary>
/// Pushes <c>request_id</c> onto the Serilog LogContext so every log line written
/// during this request — not just the final HTTP-summary line — carries it. Must be
/// registered before any other middleware so the scope wraps the whole pipeline.
///
/// Also pushes <c>trace_id</c>/<c>span_id</c> from <see cref="Activity.Current"/>.
/// These are NOT the same value as <c>request_id</c> — verified empirically
/// (docs/claude/architecture.md Decisions Log, "Local observability stack" entry):
/// <c>HttpContext.TraceIdentifier</c> is ASP.NET Core's own connection-based
/// identifier (e.g. "0HNNB64CAFRFC:00000001"), while <c>trace_id</c> is the W3C/OTel
/// trace ID Tempo indexes spans by. Both are needed to jump from a Loki log line to
/// its Tempo trace.
/// </summary>
public class RequestIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using (LogContext.PushProperty("request_id", context.TraceIdentifier))
        using (LogContext.PushProperty("trace_id", Activity.Current?.TraceId.ToHexString()))
        using (LogContext.PushProperty("span_id", Activity.Current?.SpanId.ToHexString()))
            await next(context);
    }
}
