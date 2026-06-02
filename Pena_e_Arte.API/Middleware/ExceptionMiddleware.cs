using System.Text.Json;
using FluentValidation;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.API.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        (int statusCode, string message) = ex switch
        {
            ValidationException ve          => (StatusCodes.Status400BadRequest,
                                                string.Join("; ", ve.Errors.Select(e => e.ErrorMessage))),
            NotFoundException               => (StatusCodes.Status404NotFound,              ex.Message),
            SlotAlreadyBookedException      => (StatusCodes.Status409Conflict,              ex.Message),
            DesignAlreadyApprovedException  => (StatusCodes.Status409Conflict,              ex.Message),
            TenantSuspendedException        => (StatusCodes.Status403Forbidden,             ex.Message),
            SubscriptionRequiredException   => (StatusCodes.Status402PaymentRequired,       ex.Message),
            BusinessRuleViolationException  => (StatusCodes.Status422UnprocessableEntity,   ex.Message),
            UnauthorizedAccessException     => (StatusCodes.Status401Unauthorized,          ex.Message),
            _                               => (StatusCodes.Status500InternalServerError,   "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = statusCode, message }));
    }
}
