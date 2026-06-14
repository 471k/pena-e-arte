using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Pena_e_Arte.Domain.Exceptions;
using Stripe;

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
            BadHttpRequestException          => (StatusCodes.Status400BadRequest,         "Invalid or missing request body."),
            JsonException                   => (StatusCodes.Status400BadRequest,         "Invalid JSON in request body."),
            ValidationException ve          => (StatusCodes.Status422UnprocessableEntity,
                                                string.Join("; ", ve.Errors.Select(e => e.ErrorMessage))),
            NotFoundException               => (StatusCodes.Status404NotFound,              ex.Message),
            SlotAlreadyBookedException      => (StatusCodes.Status409Conflict,              ex.Message),
            // Unique-index race (e.g. two payment attempts for one appointment) — 1062 = duplicate key
            DbUpdateException { InnerException: MySqlException { Number: 1062 } }
                                            => (StatusCodes.Status409Conflict,
                                                "This action was already completed by another request. Refresh and try again."),
            DesignAlreadyApprovedException      => (StatusCodes.Status409Conflict,              ex.Message),
            ConsentFormAlreadySignedException   => (StatusCodes.Status409Conflict,              ex.Message),
            TenantSuspendedException            => (StatusCodes.Status403Forbidden,           ex.Message),
            SubscriptionRequiredException       => (StatusCodes.Status402PaymentRequired,     ex.Message),
            BusinessRuleViolationException      => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            ServiceUnavailableException         => (StatusCodes.Status503ServiceUnavailable,  ex.Message),
            UnauthorizedAccessException     => (StatusCodes.Status401Unauthorized,          ex.Message),
            StripeException stripeEx        => (StatusCodes.Status502BadGateway,
                                                stripeEx.StripeError?.Message ?? stripeEx.Message),
            _                               => (StatusCodes.Status500InternalServerError,   "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = statusCode, message }));
    }
}
