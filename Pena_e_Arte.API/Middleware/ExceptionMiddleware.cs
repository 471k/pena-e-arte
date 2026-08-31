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
        (int statusCode, string message, string? code) = ex switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid or missing request body.", (string?)null),
            JsonException => (StatusCodes.Status400BadRequest, "Invalid JSON in request body.", null),
            ValidationException ve => (StatusCodes.Status422UnprocessableEntity,
                                                string.Join("; ", ve.Errors.Select(e => e.ErrorMessage)), null),
            NotFoundException => (StatusCodes.Status404NotFound, ex.Message, null),
            ConflictException => (StatusCodes.Status409Conflict, ex.Message, null),
            SlotAlreadyBookedException => (StatusCodes.Status409Conflict, ex.Message, null),
            AccountAlreadyExistsException => (StatusCodes.Status409Conflict, ex.Message, "ACCOUNT_ALREADY_EXISTS"),
            DuplicateNiptException => (StatusCodes.Status409Conflict, ex.Message, null),
            // Unique-index race (e.g. two payment attempts for one appointment) — 1062 = duplicate key
            DbUpdateException { InnerException: MySqlException { Number: 1062 } }
                                            => (StatusCodes.Status409Conflict,
                                                "This action was already completed by another request. Refresh and try again.", null),
            DesignAlreadyApprovedException => (StatusCodes.Status409Conflict, ex.Message, null),
            ConsentFormAlreadySignedException => (StatusCodes.Status409Conflict, ex.Message, null),
            ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message, null),
            TenantSuspendedException => (StatusCodes.Status403Forbidden, ex.Message, "STUDIO_SUSPENDED"),
            SubscriptionRequiredException => (StatusCodes.Status402PaymentRequired, ex.Message, null),
            PlanLimitExceededException => (StatusCodes.Status403Forbidden, ex.Message, "PLAN_LIMIT_EXCEEDED"),
            ManualReminderQuotaExceededException => (StatusCodes.Status429TooManyRequests, ex.Message, "MANUAL_REMINDER_QUOTA_EXCEEDED"),
            PasswordResetTokenInvalidException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "RESET_TOKEN_INVALID"),
            ChangeEmailTokenInvalidException => (StatusCodes.Status422UnprocessableEntity, ex.Message, "CHANGE_EMAIL_TOKEN_INVALID"),
            BusinessRuleViolationException => (StatusCodes.Status422UnprocessableEntity, ex.Message, null),
            ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, ex.Message, null),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, ex.Message, null),
            StripeException stripeEx => (StatusCodes.Status502BadGateway,
                                                stripeEx.StripeError?.Message ?? stripeEx.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null)
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        object body = code is not null
            ? new { status = statusCode, message, code }
            : new { status = statusCode, message };

        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
